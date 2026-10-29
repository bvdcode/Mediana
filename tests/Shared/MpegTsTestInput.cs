// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Containers.Mp4;
using Mediana.Containers.MpegTs;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class MpegTsTestInput
    {
        public static async Task<byte[]> VerifyAsync(string root, string file, Mp4TimestampMode timestampMode = Mp4TimestampMode.Media)
        {
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            using MemoryStream destination = new();
            List<(int Stream, byte[] Data, long Pts, long Dts)> expected = [];
            IReadOnlyList<EncodedStreamInfo> streams;
            await using (RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, file))))
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, new() { TimestampMode = timestampMode }))
            await using (MpegTsWriter writer = MpegTsWriter.Create(destination, reader.Streams, allocator))
            {
                streams = reader.Streams;
                while (await reader.ReadPacketAsync() is MediaPacket packet)
                {
                    using (packet)
                    {
                        expected.Add((packet.StreamIndex, packet.Data.ToArray(), Ticks(packet.PresentationTime), Ticks(packet.DecodeTime)));
                        await writer.WritePacketAsync(packet);
                    }
                }
                await writer.CompleteAsync();
            }
            if (allocator.AllocatedBytes != 0 || !destination.CanWrite)
            {
                throw new InvalidOperationException("TS remux retained buffers or closed the borrowed destination.");
            }
            byte[] result = destination.ToArray();
            Verify(result, streams, expected);
            return result;
        }

        public static void Verify(ReadOnlySpan<byte> data, IReadOnlyList<EncodedStreamInfo> streams,
            IReadOnlyList<(int Stream, byte[] Data, long Pts, long Dts)> expected)
        {
            if (data.Length == 0 || data.Length % 188 != 0)
            {
                throw new InvalidDataException("The transport stream is not a sequence of complete 188-byte packets.");
            }
            Dictionary<int, int> counters = [];
            Dictionary<int, MemoryStream> pending = [];
            List<(int Stream, byte[] Data, long Pts, long Dts)> actual = [];
            long? lastClock = null;
            int patCount = 0;
            int pmtCount = 0;
            try
            {
                for (int offset = 0; offset < data.Length; offset += 188)
                {
                    ReadOnlySpan<byte> packet = data.Slice(offset, 188);
                    int pid = (packet[1] & 31) << 8 | packet[2];
                    bool payload = (packet[3] & 0x10) != 0;
                    int counter = packet[3] & 15;
                    if (packet[0] != 0x47 || (packet[1] & 0x80) != 0 || (packet[3] & 0xC0) != 0 || (packet[3] & 0x30) == 0 ||
                        (counters.TryGetValue(pid, out int previous) && counter != ((previous + (payload ? 1 : 0)) & 15)))
                    {
                        throw new InvalidDataException("A TS header or continuity counter is invalid.");
                    }
                    counters[pid] = counter;
                    int position = 4;
                    if ((packet[3] & 0x20) != 0)
                    {
                        int length = packet[4];
                        if (length > 183)
                        {
                            throw new InvalidDataException("The TS adaptation field is too long.");
                        }
                        if (length > 0 && (packet[5] & 0x10) != 0)
                        {
                            if (length < 7 || (packet[10] & 0x7F) != 0x7E || packet[11] != 0)
                            {
                                throw new InvalidDataException("The TS PCR encoding is invalid.");
                            }
                            long clock = (long)packet[6] << 25 | (long)packet[7] << 17 | (long)packet[8] << 9 | (long)packet[9] << 1 | (long)packet[10] >> 7;
                            if (lastClock.HasValue && (clock <= lastClock.Value || clock - lastClock.Value > 3600))
                            {
                                throw new InvalidDataException("The TS PCR timeline is not increasing with a bounded 40 ms interval.");
                            }
                            lastClock = clock;
                        }
                        position += 1 + length;
                    }
                    if (!payload)
                    {
                        continue;
                    }
                    ReadOnlySpan<byte> bytes = packet[position..];
                    if (pid is 0 or 0x1000)
                    {
                        if ((packet[1] & 0x40) == 0 || bytes[0] != 0)
                        {
                            throw new InvalidDataException("A program table has no section start.");
                        }
                        int length = 3 + ((bytes[2] & 15) << 8 | bytes[3]);
                        ReadOnlySpan<byte> section = bytes.Slice(1, length);
                        if (MpegTsTables.Crc(section) != 0)
                        {
                            throw new InvalidDataException("A program table CRC is invalid.");
                        }
                        if (pid == 0)
                        {
                            patCount++;
                            if (!section[..12].SequenceEqual(Convert.FromHexString("00B00D0001C100000001F000")))
                            {
                                throw new InvalidDataException("The PAT does not identify the expected program map.");
                            }
                        }
                        else
                        {
                            pmtCount++;
                            if (section[0] != 2 || section.Length != 16 + streams.Count * 5)
                            {
                                throw new InvalidDataException("The PMT has an unexpected stream count.");
                            }
                            for (int index = 0; index < streams.Count; index++)
                            {
                                int entry = 12 + index * 5;
                                byte type = streams[index].CodecId switch
                                {
                                    "h264" => 0x1B,
                                    "aac" => 0x0F,
                                    _ => throw new InvalidDataException("The reference codec is unsupported.")
                                };
                                if (section[entry] != type || ((section[entry + 1] & 31) << 8 | section[entry + 2]) != 0x100 + index)
                                {
                                    throw new InvalidDataException("The PMT stream type or PID differs from the reference.");
                                }
                            }
                        }
                        continue;
                    }
                    int ordinal = pid - 0x100;
                    if (ordinal < 0 || ordinal >= streams.Count)
                    {
                        throw new InvalidDataException("The TS media PID is not declared.");
                    }
                    if ((packet[1] & 0x40) != 0)
                    {
                        if (pending.Remove(pid, out MemoryStream? previousPes))
                        {
                            using (previousPes)
                            {
                                AddPes(actual, streams[ordinal].StreamIndex, previousPes.ToArray());
                            }
                        }
                        pending.Add(pid, new());
                    }
                    if (!pending.TryGetValue(pid, out MemoryStream? pes))
                    {
                        throw new InvalidDataException("The TS contains media before a PES start.");
                    }
                    pes.Write(bytes);
                }
                foreach ((int pid, MemoryStream pes) in pending)
                {
                    AddPes(actual, streams[pid - 0x100].StreamIndex, pes.ToArray());
                }
            }
            finally
            {
                foreach (MemoryStream pes in pending.Values)
                {
                    pes.Dispose();
                }
            }
            if (actual.Count != expected.Count || patCount == 0 || pmtCount != patCount || !lastClock.HasValue)
            {
                throw new InvalidDataException("The TS lost packets, tables or its program clock.");
            }
            foreach (EncodedStreamInfo stream in streams)
            {
                (int Stream, byte[] Data, long Pts, long Dts)[] originals = expected.Where(packet => packet.Stream == stream.StreamIndex).ToArray();
                (int Stream, byte[] Data, long Pts, long Dts)[] outputs = actual.Where(packet => packet.Stream == stream.StreamIndex).ToArray();
                if (originals.Length != outputs.Length)
                {
                    throw new InvalidDataException("The TS stream packet count differs from the reference.");
                }
                for (int index = 0; index < originals.Length; index++)
                {
                    if (originals[index].Pts != outputs[index].Pts || originals[index].Dts != outputs[index].Dts)
                    {
                        throw new InvalidDataException("The TS PTS/DTS differs from the exact shifted reference.");
                    }
                    VerifyElementary(stream, originals[index].Data, outputs[index].Data);
                }
            }
        }

        private static void AddPes(List<(int Stream, byte[] Data, long Pts, long Dts)> packets, int stream, byte[] bytes)
        {
            int length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4));
            int flags = bytes[7] >> 6;
            if (!bytes.AsSpan(0, 3).SequenceEqual(new byte[] { 0, 0, 1 }) || (length != 0 && length + 6 != bytes.Length) || flags is not (2 or 3))
            {
                throw new InvalidDataException("The PES prefix, length or timestamp flags are invalid.");
            }
            long pts = ReadTime(bytes.AsSpan(9), flags == 2 ? 2 : 3);
            long dts = flags == 3 ? ReadTime(bytes.AsSpan(14), 1) : pts;
            packets.Add((stream, bytes[(9 + bytes[8])..], pts, dts));
        }

        private static long ReadTime(ReadOnlySpan<byte> data, int prefix)
        {
            if (data[0] >> 4 != prefix || (data[0] & data[2] & data[4] & 1) != 1)
            {
                throw new InvalidDataException("A PES timestamp marker is invalid.");
            }
            return (long)(data[0] & 14) << 29 | (long)data[1] << 22 | (long)(data[2] & 0xFE) << 14 | (long)data[3] << 7 | (long)data[4] >> 1;
        }

        private static long Ticks(MediaTimestamp? timestamp)
        {
            MediaTimestamp value = timestamp ?? throw new InvalidDataException("The input timestamp is missing.");
            return (long)decimal.Round((decimal)value.Value * value.TimeBase.Numerator * 90000 / value.TimeBase.Denominator,
                0, MidpointRounding.ToEven) + 90000;
        }

        private static void VerifyElementary(EncodedStreamInfo stream, byte[] original, byte[] output)
        {
            switch (stream.CodecId)
            {
                case "aac":
                    int length = (output[3] & 3) << 11 | output[4] << 3 | output[5] >> 5;
                    if (output[0] != 0xFF || output[1] != 0xF1 || length != output.Length || !original.AsSpan().SequenceEqual(output.AsSpan(7)))
                    {
                        throw new InvalidDataException("The ADTS frame changed the raw AAC access unit.");
                    }
                    break;
                case "h264":
                    int lengthSize = (stream.Configuration.Span[4] & 3) + 1;
                    List<byte[]> nals = [];
                    int offset = 0;
                    while (offset < original.Length)
                    {
                        int nalLength = 0;
                        for (int index = 0; index < lengthSize; index++)
                        {
                            nalLength = nalLength << 8 | original[offset++];
                        }
                        byte[] nal = original.AsSpan(offset, nalLength).ToArray();
                        offset += nalLength;
                        if ((nal[0] & 31) != 9)
                        {
                            nals.Add(nal);
                        }
                    }
                    List<byte[]> result = [];
                    offset = 0;
                    while (offset < output.Length)
                    {
                        if (!output.AsSpan(offset, 4).SequenceEqual(new byte[] { 0, 0, 0, 1 }))
                        {
                            throw new InvalidDataException("The TS AVC payload has an invalid Annex B delimiter.");
                        }
                        offset += 4;
                        int next = output.AsSpan(offset).IndexOf(new byte[] { 0, 0, 0, 1 });
                        int count = next < 0 ? output.Length - offset : next;
                        byte[] nal = output.AsSpan(offset, count).ToArray();
                        offset += count;
                        if ((nal[0] & 31) is not (7 or 8 or 9))
                        {
                            result.Add(nal);
                        }
                    }
                    byte[][] expected = nals.Where(nal => (nal[0] & 31) is not (7 or 8)).ToArray();
                    if (result.Count != expected.Length || expected.Where((nal, index) => !nal.AsSpan().SequenceEqual(result[index])).Any())
                    {
                        throw new InvalidDataException("Annex B framing changed an AVC slice or auxiliary NAL.");
                    }
                    break;
                default:
                    throw new InvalidDataException("The reference codec is unsupported.");
            }
        }
    }
}

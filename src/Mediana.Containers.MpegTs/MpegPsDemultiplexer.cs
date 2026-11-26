// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Containers.MpegTs
{
    internal class MpegPsDemultiplexer(IRandomAccessByteSource source)
    {
        private readonly long length = source.Length!.Value;
        private readonly Dictionary<int, MpegSystemsTrack> tracks = [];
        private readonly List<MpegSystemsPacket> packets = [];
        private readonly byte[] header = new byte[MpegPesHeader.MaximumHeaderLength + 4];
        private readonly Dictionary<int, int> streamTypes = [];

        public static async ValueTask<IContainerReader> OpenAsync(IRandomAccessByteSource source,
            MediaBufferAllocator allocator, CancellationToken token)
        {
            MpegPsDemultiplexer demultiplexer = new(source);
            await demultiplexer.ScanAsync(allocator, token).ConfigureAwait(false);
            if (demultiplexer.tracks.Count == 0)
            {
                throw new InvalidDataException("No recognized media packets were found in the MPEG program stream.");
            }
            return await MpegSystemsReader.CreateAsync(source, allocator, "mpegps", demultiplexer.tracks.Values, demultiplexer.packets, token).ConfigureAwait(false);
        }

        private async ValueTask ScanAsync(MediaBufferAllocator allocator, CancellationToken token)
        {
            using WritableMediaBuffer scan = allocator.Allocate(64 * 1024);
            long offset = 0;
            while (offset < length)
            {
                int count = (int)Math.Min(header.Length, length - offset);
                if (count < 4) { throw new InvalidDataException("The MPEG program stream ends inside a start code."); }
                await source.ReadExactlyAtAsync(offset, header.AsMemory(0, count), token).ConfigureAwait(false);
                if (header[0] != 0 || header[1] != 0 || header[2] != 1 || header[3] < 0xb9)
                {
                    throw new InvalidDataException("Invalid MPEG program stream start code.");
                }
                int id = header[3];
                if (id == 0xba)
                {
                    offset += ReadPackLength(header.AsSpan(0, count));
                    continue;
                }
                if (id == 0xb9)
                {
                    offset += 4;
                    continue;
                }
                if (count < 6) { throw new InvalidDataException("Truncated MPEG system packet header."); }
                int size = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4));
                long end = offset + 6 + size;
                if (end > length) { throw new InvalidDataException("The MPEG system packet exceeds its source."); }
                if (id == 0xbc)
                {
                    await ReadProgramMapAsync(offset + 6, size, allocator, token).ConfigureAwait(false);
                }
                else if (id is 0xbd or >= 0xc0 and <= 0xef)
                {
                    MpegPesHeader pes = MpegPesHeader.Read(header.AsSpan(0, count));
                    if (pes.PayloadLength is null)
                    {
                        if (id < 0xe0) { throw new InvalidDataException("Only video can use an unbounded MPEG PES length."); }
                        end = await FindNextPacketAsync(offset + pes.HeaderLength, scan.Memory, token).ConfigureAwait(false);
                    }
                    int prefixCount = Math.Min(Math.Min(count, checked((int)(end - offset))) - pes.HeaderLength, 4);
                    if (prefixCount < 0) { throw new InvalidDataException("Truncated MPEG program stream PES payload."); }
                    AddPacket(offset, end, pes, header.AsSpan(pes.HeaderLength, prefixCount));
                }
                offset = end;
            }
        }

        private void AddPacket(long position, long end, MpegPesHeader header, ReadOnlySpan<byte> prefix)
        {
            int key = header.StreamId;
            int skip = 0;
            MediaKind kind;
            string? codec;
            string framing;
            if (key == 0xbd)
            {
                if (prefix.Length < 4) { return; }
                int substream = prefix[0];
                (codec, framing) = substream switch
                {
                    >= 0x80 and <= 0x87 => ("ac3", "ac3.syncframes"),
                    >= 0x88 and <= 0x8f => ("dts", "dts.syncframes"),
                    _ => (null, string.Empty)
                };
                kind = MediaKind.Audio;
                skip = 4;
                key = key << 8 | substream;
            }
            else if (key >= 0xe0)
            {
                kind = MediaKind.Video;
                (codec, framing) = streamTypes.GetValueOrDefault(key) switch
                {
                    0x01 => ("mpeg1video", "mpeg1.video"),
                    0x10 => ("mpeg4-visual", "mpeg4.visual"),
                    0x1b => ("h264", "avc.annex-b"),
                    0x24 => ("hevc", "hevc.annex-b"),
                    0x02 => ("mpeg2video", "mpeg.video"),
                    _ => ("mpeg-video", "mpeg.video")
                };
            }
            else
            {
                kind = MediaKind.Audio;
                (codec, framing) = streamTypes.GetValueOrDefault(key) switch
                {
                    0x0f => ("aac", "aac.adts"),
                    0x11 => ("aac", "aac.latm"),
                    _ => ("mpeg-audio", "mpeg.audio")
                };
            }
            if (codec is null) { return; }
            if (!tracks.TryGetValue(key, out MpegSystemsTrack? track))
            {
                track = new(tracks.Count, kind, codec, framing);
                tracks.Add(key, track);
            }
            if (track.Add(position, [new(position, checked((int)(end - position)))], header, null, skip) is MpegSystemsPacket packet)
            {
                packets.Add(packet);
            }
        }

        private async ValueTask ReadProgramMapAsync(long offset, int length, MediaBufferAllocator allocator, CancellationToken token)
        {
            if (length < 10) { throw new InvalidDataException("Truncated MPEG program stream map."); }
            using WritableMediaBuffer buffer = allocator.Allocate(length);
            await source.ReadExactlyAtAsync(offset, buffer.Memory, token).ConfigureAwait(false);
            ReadOnlySpan<byte> map = buffer.Span;
            if ((map[0] & 0x80) == 0) { return; }
            int start = 4 + BinaryPrimitives.ReadUInt16BigEndian(map[2..]);
            if (start > map.Length - 6) { throw new InvalidDataException("Invalid MPEG program stream map descriptors."); }
            int size = BinaryPrimitives.ReadUInt16BigEndian(map[start..]);
            int position = start + 2;
            int end = position + size;
            if (end != map.Length - 4) { throw new InvalidDataException("Invalid MPEG program stream map length."); }
            while (position < end)
            {
                if (end - position < 4) { throw new InvalidDataException("Truncated MPEG program stream map entry."); }
                int descriptors = BinaryPrimitives.ReadUInt16BigEndian(map[(position + 2)..]);
                if (descriptors > end - position - 4)
                {
                    throw new InvalidDataException("MPEG program stream map entry exceeds its declared length.");
                }
                if (tracks.ContainsKey(map[position + 1]) &&
                    streamTypes.GetValueOrDefault(map[position + 1]) != map[position])
                {
                    throw new NotSupportedException("The MPEG program stream map changes an active stream type.");
                }
                streamTypes[map[position + 1]] = map[position];
                position += 4 + descriptors;
            }
        }

        private async ValueTask<long> FindNextPacketAsync(long offset, Memory<byte> buffer, CancellationToken token)
        {
            for (long cursor = offset; length - cursor >= 4;)
            {
                int count = (int)Math.Min(buffer.Length, length - cursor);
                await source.ReadExactlyAtAsync(cursor, buffer[..count], token).ConfigureAwait(false);
                ReadOnlySpan<byte> span = buffer.Span[..count];
                for (int index = 0; index <= span.Length - 4; index++)
                {
                    if (span[index] == 0 && span[index + 1] == 0 && span[index + 2] == 1 && span[index + 3] >= 0xb9)
                    {
                        return cursor + index;
                    }
                }
                cursor += count - 3;
            }
            return length;
        }

        private static int ReadPackLength(ReadOnlySpan<byte> pack)
        {
            if (pack.Length >= 12 && (pack[4] & 0xf0) == 0x20) { return 12; }
            if (pack.Length >= 14 && (pack[4] & 0xc0) == 0x40)
            {
                int length = 14 + (pack[13] & 7);
                if (length <= pack.Length) { return length; }
            }
            throw new InvalidDataException("Invalid MPEG program stream pack header.");
        }
    }
}

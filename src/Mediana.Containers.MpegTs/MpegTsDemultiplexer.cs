// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Containers.MpegTs
{
    internal class MpegTsDemultiplexer
    {
        private readonly Dictionary<int, MpegTsSectionBuffer> tables = new() { [0] = new() };
        private readonly Dictionary<int, MpegSystemsTrack> tracks = [];
        private readonly Dictionary<int, MpegPesAssembly> pending = [];
        private readonly Dictionary<int, int> continuity = [];
        private readonly List<MpegSystemsPacket> packets = [];

        public static async ValueTask<IContainerReader> OpenAsync(IRandomAccessByteSource source,
            MediaBufferAllocator allocator, int stride, int syncOffset, CancellationToken token)
        {
            MpegTsDemultiplexer demultiplexer = new();
            long length = source.Length!.Value;
            if (length % stride != 0)
            {
                throw new InvalidDataException("MPEG-TS ends with a partial transport packet.");
            }
            using WritableMediaBuffer block = allocator.Allocate(stride * 512);
            for (long position = 0; position < length;)
            {
                int count = (int)Math.Min(block.Memory.Length, length - position);
                await source.ReadExactlyAtAsync(position, block.Memory[..count], token).ConfigureAwait(false);
                for (int offset = 0; offset < count; offset += stride)
                {
                    demultiplexer.Accept(block.Span.Slice(offset + syncOffset, 188), position + offset + syncOffset);
                }
                position += count;
            }
            foreach (int pid in demultiplexer.pending.Keys.ToArray())
            {
                demultiplexer.Finish(pid);
            }
            if (demultiplexer.tracks.Count == 0)
            {
                throw new InvalidDataException("No MPEG-TS program map describes media streams.");
            }
            return await MpegSystemsReader.CreateAsync(source, allocator, "mpegts", demultiplexer.tracks.Values, demultiplexer.packets, token).ConfigureAwait(false);
        }

        private void Accept(ReadOnlySpan<byte> packet, long position)
        {
            if (packet[0] != 0x47)
            {
                throw new InvalidDataException("Invalid MPEG-TS packet sync byte.");
            }
            int pid = ReadPid(packet[1..]);
            if (!tables.ContainsKey(pid) && !tracks.ContainsKey(pid)) { return; }
            if ((packet[1] & 0x80) != 0 || (packet[3] & 0xC0) != 0)
            {
                throw new NotSupportedException("Errored or scrambled MPEG-TS packets are not supported.");
            }
            int control = packet[3] >> 4 & 3;
            int offset = 4;
            int flags = 0;
            if (control == 0) { throw new InvalidDataException("Invalid MPEG-TS adaptation field control."); }
            if ((control & 2) != 0)
            {
                offset = 5 + packet[4];
                if (offset > packet.Length)
                {
                    throw new InvalidDataException("The MPEG-TS adaptation field exceeds its packet.");
                }
                if (offset > 5) { flags = packet[5]; }
            }
            if ((flags & 0x80) != 0)
            {
                continuity.Remove(pid);
                if (pending.ContainsKey(pid))
                {
                    Finish(pid);
                }
            }
            if ((control & 1) == 0 || offset == packet.Length) { return; }
            int counter = packet[3] & 15;
            if (continuity.TryGetValue(pid, out int previous))
            {
                if (counter == previous) { return; }
                if (counter != ((previous + 1) & 15))
                {
                    throw new InvalidDataException("MPEG-TS transport packet continuity was lost.");
                }
            }
            continuity[pid] = counter;
            bool start = (packet[1] & 0x40) != 0;
            ReadOnlySpan<byte> payload = packet[offset..];
            if (tables.TryGetValue(pid, out MpegTsSectionBuffer? table))
            {
                table.Accept(payload, start, pid == 0 ? ReadPat : ReadPmt);
                return;
            }
            if (start)
            {
                Finish(pid);
                pending.Add(pid, new(position, (flags & 0x40) != 0));
            }
            if (pending.TryGetValue(pid, out MpegPesAssembly? pes))
            {
                pes.Add(position + offset, payload);
            }
        }

        private void Finish(int pid)
        {
            if (pending.Remove(pid, out MpegPesAssembly? value) && value.Finish(tracks[pid]) is MpegSystemsPacket packet)
            {
                packets.Add(packet);
            }
        }

        private void ReadPat(ReadOnlyMemory<byte> memory)
        {
            ReadOnlySpan<byte> section = memory.Span;
            if (section[0] != 0 || section.Length < 12 || (section.Length - 12) % 4 != 0)
            {
                throw new InvalidDataException("Invalid MPEG-TS program association table.");
            }
            if ((section[5] & 1) == 0) { return; }
            for (int offset = 8; offset < section.Length - 4; offset += 4)
            {
                if (BinaryPrimitives.ReadUInt16BigEndian(section[offset..]) == 0) { continue; }
                int pid = ReadPid(section[(offset + 2)..]);
                if (!tables.ContainsKey(pid)) { tables.Add(pid, new()); }
            }
        }

        private void ReadPmt(ReadOnlyMemory<byte> memory)
        {
            ReadOnlySpan<byte> section = memory.Span;
            if (section[0] != 2 || section.Length < 16)
            {
                throw new InvalidDataException("Invalid MPEG-TS program map table.");
            }
            if ((section[5] & 1) == 0) { return; }
            int end = section.Length - 4;
            int position = 12 + ReadDescriptorLength(section[10..]);
            if (position > end) { throw new InvalidDataException("MPEG-TS program descriptors exceed their table."); }
            while (position < end)
            {
                if (end - position < 5) { throw new InvalidDataException("Truncated MPEG-TS stream entry."); }
                int type = section[position];
                int pid = ReadPid(section[(position + 1)..]);
                int size = ReadDescriptorLength(section[(position + 3)..]);
                position += 5;
                if (size > end - position) { throw new InvalidDataException("MPEG-TS stream descriptors exceed their table."); }
                (string? language, int privateType) = ReadDescriptors(section.Slice(position, size));
                (MediaKind kind, string? codec, string framing) = Describe(type == 6 ? privateType : type);
                if (codec is not null && !tracks.ContainsKey(pid))
                {
                    tracks.Add(pid, new(tracks.Count, kind, codec, framing, language));
                }
                position += size;
            }
        }

        private static (string? Language, int PrivateType) ReadDescriptors(ReadOnlySpan<byte> data)
        {
            string? language = null;
            int type = 0;
            while (!data.IsEmpty)
            {
                if (data.Length < 2 || data[1] > data.Length - 2)
                {
                    throw new InvalidDataException("Truncated MPEG-TS descriptor.");
                }
                int size = data[1];
                ReadOnlySpan<byte> value = data.Slice(2, size);
                switch (data[0])
                {
                    case 0x0a when size >= 4:
                        string identifier = Encoding.ASCII.GetString(value[..3]);
                        if (!string.IsNullOrWhiteSpace(identifier)) { language ??= identifier; }
                        break;
                    case 0x6a:
                        type = 0x81;
                        break;
                    case 0x7a:
                        type = 0x87;
                        break;
                    case 0x05 when value.StartsWith("AC-3"u8):
                        type = 0x81;
                        break;
                    case 0x05 when value.StartsWith("EAC3"u8):
                        type = 0x87;
                        break;
                }
                data = data[(size + 2)..];
            }
            return (language, type);
        }

        private static (MediaKind Kind, string? Codec, string Framing) Describe(int type) => type switch
        {
            0x01 => (MediaKind.Video, "mpeg1video", "mpeg1.video"),
            0x02 => (MediaKind.Video, "mpeg2video", "mpeg2.video"),
            0x03 or 0x04 => (MediaKind.Audio, "mpeg-audio", "mpeg.audio"),
            0x0f => (MediaKind.Audio, "aac", "aac.adts"),
            0x10 => (MediaKind.Video, "mpeg4-visual", "mpeg4.visual"),
            0x11 => (MediaKind.Audio, "aac", "aac.latm"),
            0x1b => (MediaKind.Video, "h264", "avc.annex-b"),
            0x24 => (MediaKind.Video, "hevc", "hevc.annex-b"),
            0x81 => (MediaKind.Audio, "ac3", "ac3.syncframes"),
            0x87 => (MediaKind.Audio, "eac3", "eac3.syncframes"),
            _ => (MediaKind.Data, null, string.Empty)
        };

        private static int ReadPid(ReadOnlySpan<byte> data) => BinaryPrimitives.ReadUInt16BigEndian(data) & 0x1fff;
        private static int ReadDescriptorLength(ReadOnlySpan<byte> data) => BinaryPrimitives.ReadUInt16BigEndian(data) & 0x0fff;
    }
}

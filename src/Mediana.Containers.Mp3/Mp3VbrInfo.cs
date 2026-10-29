// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core.Audio;

namespace Mediana.Containers.Mp3
{
    /// <summary>Declared Xing/Info statistics. Frame count excludes the metadata frame.
    /// No encoder delay, end padding, seek accuracy or gapless playback is inferred.</summary>
    public record Mp3VbrInfo(string Kind, uint? FrameCount, uint? ByteCount, uint? Quality)
    {
        internal static Mp3VbrInfo? Read(ReadOnlySpan<byte> frame, Mp3FrameHeader header)
        {
            int position = Mp3FrameHeader.HeaderBytes + header.SideInfoBytes;
            if (frame.Length - position < 8)
            {
                return null;
            }
            string kind;
            if (frame.Slice(position, 4).SequenceEqual("Xing"u8))
            {
                kind = "Xing";
            }
            else if (frame.Slice(position, 4).SequenceEqual("Info"u8))
            {
                kind = "Info";
            }
            else
            {
                if (frame.Length >= 40 && frame.Slice(36, 4).SequenceEqual("VBRI"u8))
                {
                    throw new NotSupportedException("VBRI metadata frames are not supported.");
                }
                return null;
            }
            position += 4;
            uint flags = ReadField(frame, ref position);
            if ((flags & ~15u) != 0)
            {
                throw new InvalidDataException("The Xing header contains reserved flags.");
            }
            uint? frames = (flags & 1) != 0 ? ReadField(frame, ref position) : null;
            uint? bytes = (flags & 2) != 0 ? ReadField(frame, ref position) : null;
            if ((flags & 4) != 0)
            {
                if (frame.Length - position < 100)
                {
                    throw new InvalidDataException("The Xing seek table is truncated.");
                }
                position += 100;
            }
            uint? quality = (flags & 8) != 0 ? ReadField(frame, ref position) : null;
            if (frames == 0 || bytes == 0)
            {
                throw new InvalidDataException("The Xing frame or byte count must be positive when present.");
            }
            return new Mp3VbrInfo(kind, frames, bytes, quality);
        }

        private static uint ReadField(ReadOnlySpan<byte> data, ref int position)
        {
            if (data.Length - position < 4)
            {
                throw new InvalidDataException("The Xing header is truncated.");
            }
            uint value = BinaryPrimitives.ReadUInt32BigEndian(data[position..]);
            position += 4;
            return value;
        }
    }
}

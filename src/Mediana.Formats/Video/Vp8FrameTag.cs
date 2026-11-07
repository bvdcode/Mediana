// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Video
{
    /// <summary>The fixed three-byte VP8 frame tag; parsing does not require the compressed partitions.</summary>
    public readonly record struct Vp8FrameTag(bool KeyFrame, bool ShowFrame, int Version, int ControlLength)
    {
        public const int HeaderBytes = 3;

        public static Vp8FrameTag Read(ReadOnlySpan<byte> prefix, int frameLength)
        {
            if (prefix.Length < HeaderBytes) { throw new InvalidDataException("Truncated VP8 frame tag."); }
            int tag = prefix[0] | prefix[1] << 8 | prefix[2] << 16;
            bool key = (tag & 1) == 0;
            int version = (tag >> 1) & 7, offset = key ? 10 : HeaderBytes, length = tag >> 5;
            if (version > 3) { throw new NotSupportedException("The VP8 frame version is reserved."); }
            if (frameLength < offset || length < 2 || length > frameLength - offset)
            { throw new InvalidDataException("Invalid VP8 control partition extent."); }
            return new(key, (tag & 16) != 0, version, length);
        }
    }
}

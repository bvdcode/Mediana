// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Video
{
    /// <summary>VP9 display and profile flags from the first byte of an uncompressed frame header.</summary>
    public readonly record struct Vp9FramePrefix(int Profile, bool ShowExisting, bool ShowFrame)
    {
        public static Vp9FramePrefix Read(ReadOnlySpan<byte> prefix)
        {
            if (prefix.IsEmpty) { throw new InvalidDataException("Truncated VP9 frame header."); }
            byte first = prefix[0];
            if ((first >> 6) != 2) { throw new InvalidDataException("Invalid VP9 frame marker."); }
            int profile = ((first >> 5) & 1) | ((first >> 3) & 2);
            int shift = 3;
            if (profile == 3)
            {
                if ((first & 8) != 0) { throw new InvalidDataException("A VP9 reserved bit must be zero."); }
                shift--;
            }
            bool existing = (first & (1 << shift)) != 0;
            bool shown = existing || (first & (1 << (shift - 2))) != 0;
            return new(profile, existing, shown);
        }
    }
}

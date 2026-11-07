// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Video
{
    /// <summary>Locates complete MPEG-4 Visual start-code prefixes without reading past the supplied payload.</summary>
    public static class Mpeg4VisualStartCodes
    {
        public static int Find(ReadOnlySpan<byte> data, int offset = 0)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, data.Length);
            ReadOnlySpan<byte> prefix = [0, 0, 1];
            int relative = data[offset..].IndexOf(prefix);
            if (relative < 0 || offset + relative + 3 == data.Length) { return -1; }
            return offset + relative;
        }
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Mpeg2
{
    internal ref struct Mpeg2StartCodes(ReadOnlySpan<byte> data)
    {
        private static ReadOnlySpan<byte> Prefix => [0, 0, 1];
        private ReadOnlySpan<byte> remaining = data;

        public bool TryRead(out byte code, out ReadOnlySpan<byte> payload)
        {
            code = 0;
            payload = [];
            if (remaining.IsEmpty) { return false; }
            int start = remaining.IndexOf(Prefix);
            if (start < 0 || remaining.Length - start < 4 || remaining[..start].IndexOfAnyExcept((byte)0) >= 0)
            {
                throw new InvalidDataException("An MPEG-2 video start code is missing or incomplete.");
            }
            code = remaining[start + 3];
            remaining = remaining[(start + 4)..];
            int next = remaining.IndexOf(Prefix);
            int length = next < 0 ? remaining.Length : next;
            payload = remaining[..length];
            remaining = remaining[length..];
            return true;
        }
    }
}

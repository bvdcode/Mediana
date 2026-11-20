// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Codecs.OpenExr
{
    internal readonly record struct ExrWindow(int Left, int Top, int Right, int Bottom)
    {
        internal int Width => checked(Right - Left + 1);
        internal int Height => checked(Bottom - Top + 1);
        internal static ExrWindow Read(ReadOnlyMemory<byte> bytes)
        {
            if (bytes.Length != 16) { throw new InvalidDataException("Invalid OpenEXR window length."); }
            ReadOnlySpan<byte> span = bytes.Span;
            ExrWindow window = new(BinaryPrimitives.ReadInt32LittleEndian(span), BinaryPrimitives.ReadInt32LittleEndian(span[4..]),
                BinaryPrimitives.ReadInt32LittleEndian(span[8..]), BinaryPrimitives.ReadInt32LittleEndian(span[12..]));
            if (window.Right < window.Left || window.Bottom < window.Top || (long)window.Right - window.Left >= int.MaxValue
                || (long)window.Bottom - window.Top >= int.MaxValue) { throw new InvalidDataException("Invalid OpenEXR image window."); }
            return window;
        }
    }
}

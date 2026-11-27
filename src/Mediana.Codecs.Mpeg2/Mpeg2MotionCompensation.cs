// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Mpeg2
{
    internal static class Mpeg2MotionCompensation
    {
        public static void Apply(Span<byte> target, ReadOnlySpan<byte> source, Mpeg2Sequence sequence,
            int macroblock, Mpeg2MotionVector vector, bool average)
        {
            if (source.IsEmpty) { throw new InvalidDataException("An MPEG-2 predicted picture has no reference."); }
            int x = macroblock % sequence.Columns;
            int y = macroblock / sequence.Columns;
            int luma = sequence.LumaBytes;
            int chroma = luma / 4;
            WriteBlock(target[..luma], source[..luma], sequence.LumaStride,
                x * 16, y * 16, 16, vector.X, vector.Y, average);
            WriteBlock(target.Slice(luma, chroma), source.Slice(luma, chroma), sequence.ChromaStride,
                x * 8, y * 8, 8, vector.X / 2, vector.Y / 2, average);
            WriteBlock(target.Slice(luma + chroma, chroma), source.Slice(luma + chroma, chroma), sequence.ChromaStride,
                x * 8, y * 8, 8, vector.X / 2, vector.Y / 2, average);
        }

        private static void WriteBlock(Span<byte> target, ReadOnlySpan<byte> source, int stride,
            int x, int y, int size, int horizontal, int vertical, bool average)
        {
            int left = x + (horizontal >> 1);
            int top = y + (vertical >> 1);
            int halfX = horizontal & 1;
            int halfY = vertical & 1;
            if (left < 0 || top < 0 || left + size + halfX > stride || top + size + halfY > source.Length / stride)
            {
                throw new InvalidDataException("An MPEG-2 motion vector references samples outside its reconstructed picture.");
            }

            int mode = halfX | (halfY << 1);
            int from = top * stride + left;
            int to = y * stride + x;
            if (mode == 0 && !average)
            {
                for (int row = 0; row < size; row++, from += stride, to += stride)
                {
                    source.Slice(from, size).CopyTo(target[to..]);
                }
                return;
            }
            for (int row = 0; row < size; row++, from += stride, to += stride)
            {
                for (int column = 0; column < size; column++)
                {
                    int offset = from + column;
                    int value = mode switch
                    {
                        0 => source[offset],
                        1 => (source[offset] + source[offset + 1] + 1) >> 1,
                        2 => (source[offset] + source[offset + stride] + 1) >> 1,
                        3 => (source[offset] + source[offset + 1] + source[offset + stride] + source[offset + stride + 1] + 2) >> 2,
                        _ => throw new InvalidOperationException("Invalid MPEG-2 half-sample filter.")
                    };
                    int pixel = to + column;
                    target[pixel] = (byte)(average ? (target[pixel] + value + 1) >> 1 : value);
                }
            }
        }
    }
}

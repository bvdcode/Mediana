// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Jpeg
{
    internal class JpegLosslessComponent(JpegComponent component, JpegHuffmanTable table, bool interleaved,
        int precision, JpegScanParameters scan, int historyOffset)
    {
        private readonly int horizontal = interleaved ? component.Horizontal : 1;
        private readonly int vertical = interleaved ? component.Vertical : 1;
        private readonly int initial = 1 << (precision - scan.Low - 1);
        private readonly int maximum = (1 << precision) - 1;

        internal int HistoryLength => checked(component.Stride * (vertical + 1));

        internal void DecodeMcu(ref JpegEntropyReader bits, Span<ushort> history, int row, int column, int firstRow)
        {
            int stride = component.Stride;
            int start = column * horizontal;
            for (int dy = 0; dy < vertical; dy++)
            {
                int y = row * vertical + dy;
                int slot = y % (vertical + 1);
                Span<ushort> current = history.Slice(historyOffset + slot * stride, stride);
                ReadOnlySpan<ushort> above = history.Slice(historyOffset + (slot == 0 ? vertical : slot - 1) * stride, stride);
                Span<byte> output = component.Samples.Span.Slice(y * stride, stride);
                for (int x = start; x < start + horizontal; x++)
                {
                    int prediction = y == firstRow * vertical ? (x == 0 ? initial : current[x - 1])
                        : x == 0 ? above[x] : Predict(scan.First, current[x - 1], above[x], above[x - 1]);
                    int category = table.Decode(ref bits);
                    if (category > 16) { throw new InvalidDataException("Invalid lossless JPEG difference category."); }
                    int difference = category == 16 ? -32768 : bits.Receive(category);
                    ushort sample = unchecked((ushort)(prediction + difference));
                    int value = sample << scan.Low;
                    if (value > maximum) { throw new InvalidDataException("Lossless JPEG sample exceeds its declared precision."); }
                    current[x] = sample;
                    output[x] = (byte)((value * 255 + maximum / 2) / maximum);
                }
            }
        }

        private static int Predict(int predictor, int left, int above, int diagonal) => predictor switch
        {
            1 => left,
            2 => above,
            3 => diagonal,
            4 => left + above - diagonal,
            5 => left + ((above - diagonal) >> 1),
            6 => above + ((left - diagonal) >> 1),
            7 => (left + above) >> 1,
            _ => throw new InvalidDataException("Invalid lossless JPEG predictor.")
        };
    }
}

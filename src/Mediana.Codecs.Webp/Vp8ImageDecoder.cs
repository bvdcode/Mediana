// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Webp
{
    internal static partial class Vp8ImageDecoder
    {
        private static ReadOnlySpan<sbyte> BlockTree => [0, 2, -1, 4, -2, 6, 8, 12, -3, 10, -5, -6, -4, 14, -7, 16, -8, -9];
        private static ReadOnlySpan<sbyte> LumaTree => [-4, 2, 4, 6, 0, -1, -2, -3];
        private static ReadOnlySpan<byte> LumaProbabilities => [145, 156, 163, 128];
        private static ReadOnlySpan<sbyte> ChromaTree => [0, 2, -1, 4, -2, -3];
        private static ReadOnlySpan<byte> ChromaProbabilities => [142, 114, 183];
        internal static void Decode(ReadOnlyMemory<byte> encoded, Span<byte> output, int width, int height, WebpDecodeWorkspace workspace, CancellationToken token)
        {
            Vp8FrameHeader header = new(encoded, token);
            int columns = (width + 15) / 16, rows = (height + 15) / 16, stride = checked(columns * 16), paddedHeight = checked(rows * 16);
            int yLength = checked(stride * paddedHeight), uvLength = yLength / 4;
            Span<byte> planes = workspace.Allocate(checked(yLength + 2 * uvLength)).Span;
            Span<byte> yPlane = planes[..yLength], uPlane = planes.Slice(yLength, uvLength), vPlane = planes[(yLength + uvLength)..];
            Span<byte> aboveModes = workspace.Allocate(columns * 4).Span, aboveContexts = workspace.Allocate(columns * 9).Span;
            Span<byte> filters = workspace.Allocate(checked(columns * rows * 2)).Span;
            Span<byte> leftModes = stackalloc byte[4], leftContexts = stackalloc byte[9], modes = stackalloc byte[16];
            Span<int> dcValues = stackalloc int[16], walsh = stackalloc int[16], coefficients = stackalloc int[16];
            Span<byte> prediction = stackalloc byte[256], top = stackalloc byte[16], left = stackalloc byte[16];
            for (int row = 0; row < rows; row++)
            {
                leftModes.Clear(); leftContexts.Clear();
                Vp8BooleanReader tokens = header.Tokens[row & (header.Tokens.Length - 1)];
                for (int column = 0; column < columns; column++)
                {
                    token.ThrowIfCancellationRequested();
                    int segment = header.UpdateMap ? header.Control.Read(header.SegmentProbabilities[0])
                        ? 2 + (header.Control.Read(header.SegmentProbabilities[2]) ? 1 : 0)
                        : header.Control.Read(header.SegmentProbabilities[1]) ? 1 : 0 : 0;
                    bool skip = header.SkipEnabled && header.Control.Read(header.SkipProbability);
                    int mode = header.Control.Tree(LumaTree, LumaProbabilities);
                    bool blocks = mode == 4;
                    if (blocks)
                    {
                        for (int by = 0; by < 4; by++)
                        {
                            for (int bx = 0; bx < 4; bx++)
                            {
                                int index = column * 4 + bx;
                                byte blockMode = (byte)header.Control.Tree(BlockTree, Vp8ProbabilityTables.BlockModes.Slice((aboveModes[index] * 10 + leftModes[by]) * 9, 9));
                                modes[by * 4 + bx] = blockMode; aboveModes[index] = blockMode; leftModes[by] = blockMode;
                            }
                        }
                    }
                    else
                    {
                        byte blockMode = mode switch { 0 => 0, 1 => 2, 2 => 3, 3 => 1, _ => throw new InvalidDataException("Invalid VP8 luma mode.") };
                        aboveModes.Slice(column * 4, 4).Fill(blockMode); leftModes.Fill(blockMode);
                    }
                    int chromaMode = header.Control.Tree(ChromaTree, ChromaProbabilities), q = header.Quantizers[segment];
                    Span<byte> above = aboveContexts.Slice(column * 9, 9);
                    dcValues.Clear(); walsh.Clear();
                    bool any = false;
                    if (!blocks)
                    {
                        bool nonzero = !skip && Vp8CoefficientReader.Read(tokens, header.Probabilities, walsh, 1, above[8] + leftContexts[8],
                            Dc(q + header.Deltas[1]) * 2, Math.Max(8, Ac(q + header.Deltas[2]) * 155 / 100));
                        above[8] = leftContexts[8] = nonzero ? (byte)1 : (byte)0; any |= nonzero;
                        Vp8CoefficientReader.Walsh(walsh, dcValues);
                        byte corner = Neighbors(yPlane, stride, column * 16, row * 16, 16, top, left);
                        Vp8ChromaPrediction.Predict((Vp8ChromaMode)mode, top, left, corner, row != 0, column != 0, prediction, 16);
                        for (int line = 0; line < 16; line++) { prediction.Slice(line * 16, 16).CopyTo(yPlane.Slice((row * 16 + line) * stride + column * 16, 16)); }
                    }
                    for (int plane = 0; plane < 3; plane++)
                    {
                        Span<byte> pixels = plane switch { 0 => yPlane, 1 => uPlane, 2 => vPlane, _ => throw new InvalidDataException("Invalid VP8 plane.") };
                        int size = plane == 0 ? 16 : 8, planeStride = plane == 0 ? stride : stride / 2, across = size / 4;
                        int contextStart = plane switch { 0 => 0, 1 => 4, 2 => 6, _ => throw new InvalidDataException("Invalid VP8 plane.") };
                        int dc = plane == 0 ? Dc(q + header.Deltas[0]) : Math.Min(132, Dc(q + header.Deltas[3]));
                        int ac = plane == 0 ? Ac(q) : Ac(q + header.Deltas[4]);
                        if (plane != 0)
                        {
                            byte corner = Neighbors(pixels, planeStride, column * size, row * size, size, top[..size], left[..size]);
                            Vp8ChromaPrediction.Predict((Vp8ChromaMode)chromaMode, top[..size], left[..size], corner, row != 0, column != 0, prediction[..64]);
                            for (int line = 0; line < size; line++) { prediction.Slice(line * size, size).CopyTo(pixels.Slice((row * size + line) * planeStride + column * size, size)); }
                        }
                        for (int by = 0; by < across; by++)
                        {
                            for (int bx = 0; bx < across; bx++)
                            {
                                coefficients.Clear();
                                int type = plane == 0 ? blocks ? 3 : 0 : 2;
                                bool nonzero = !skip && Vp8CoefficientReader.Read(tokens, header.Probabilities, coefficients, type,
                                    above[contextStart + bx] + leftContexts[contextStart + by], dc, ac);
                                above[contextStart + bx] = leftContexts[contextStart + by] = nonzero ? (byte)1 : (byte)0;
                                any |= nonzero;
                                if (type == 0) { coefficients[0] = dcValues[by * 4 + bx]; }
                                int px = column * size + bx * 4, py = row * size + by * 4;
                                if (plane == 0 && blocks)
                                {
                                    byte corner = Neighbors(pixels, planeStride, px, py, 4, top[..4], left[..4]);
                                    for (int index = 4; index < 8; index++)
                                    {
                                        int aboveY = bx == 3 ? row * 16 - 1 : py - 1;
                                        top[index] = aboveY < 0 ? (byte)127 : pixels[aboveY * planeStride + Math.Min(px + index, planeStride - 1)];
                                    }
                                    Vp8IntraPrediction.Predict((Vp8IntraMode)modes[by * 4 + bx], top[..8], left[..4], corner, prediction[..16]);
                                }
                                else
                                { for (int line = 0; line < 4; line++) { pixels.Slice((py + line) * planeStride + px, 4).CopyTo(prediction.Slice(line * 4, 4)); } }
                                Vp8Transform.Reconstruct(coefficients, prediction, pixels, py * planeStride + px, planeStride);
                            }
                        }
                    }
                    int filterOffset = (row * columns + column) * 2;
                    filters[filterOffset] = (byte)header.Filter(segment, blocks); filters[filterOffset + 1] = blocks || any ? (byte)1 : (byte)0;
                }
            }
            Filter(yPlane, uPlane, vPlane, stride, columns, rows, filters, header, token);
            ToRgba(yPlane, uPlane, vPlane, stride, width, height, output, token);
        }
        private static byte Neighbors(ReadOnlySpan<byte> pixels, int stride, int x, int y, int size, Span<byte> top, Span<byte> left)
        {
            for (int index = 0; index < size; index++)
            {
                top[index] = y == 0 ? (byte)127 : pixels[(y - 1) * stride + x + index];
                left[index] = x == 0 ? (byte)129 : pixels[(y + index) * stride + x - 1];
            }
            return y == 0 ? (byte)127 : x == 0 ? (byte)129 : pixels[(y - 1) * stride + x - 1];
        }
        private static int Dc(int q) => Vp8ProbabilityTables.DcQuantizers[Math.Clamp(q, 0, 127)];
        private static int Ac(int q) => Vp8ProbabilityTables.AcQuantizers[Math.Clamp(q, 0, 127)];
    }
}

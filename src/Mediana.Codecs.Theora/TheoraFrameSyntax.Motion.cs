// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Theora
{
    internal static partial class TheoraFrameSyntax
    {
        private static void ReadMotion(ref TheoraBitReader reader, TheoraLayout layout, TheoraFrameState state)
        {
            bool fixedLength = reader.Flag(); int lastX = 0, lastY = 0, priorX = 0, priorY = 0;
            Span<int> luma = stackalloc int[4];
            foreach (int macro in layout.MacroOrder)
            {
                int mode = state.Modes[macro], x = 0, y = 0;
                int mx = macro % (layout.Info.CodedWidth / 16), my = macro / (layout.Info.CodedWidth / 16);
                if (mode == 7)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        int block = layout.Block(0, mx * 2 + (i & 1), my * 2 + (i >> 1)); luma[i] = block;
                        if (state.Coded[block] != 0)
                        {
                            x = ReadMotionComponent(ref reader, fixedLength); y = ReadMotionComponent(ref reader, fixedLength);
                            state.MotionX[block] = (sbyte)x; state.MotionY[block] = (sbyte)y;
                        }
                    }
                    priorX = lastX; priorY = lastY; lastX = x; lastY = y;
                    int hs = layout.Info.HorizontalSubsampling, vs = layout.Info.VerticalSubsampling;
                    for (int plane = 1; plane < 3; plane++)
                    {
                        for (int by = 0; by < 2 / vs; by++)
                        {
                            for (int bx = 0; bx < 2 / hs; bx++)
                            {
                                int sumX = 0, sumY = 0;
                                for (int dy = 0; dy < vs; dy++)
                                {
                                    for (int dx = 0; dx < hs; dx++)
                                    {
                                        int block = luma[(by * vs + dy) * 2 + bx * hs + dx];
                                        sumX += state.MotionX[block]; sumY += state.MotionY[block];
                                    }
                                }
                                int chroma = layout.Block(plane, mx * (2 / hs) + bx, my * (2 / vs) + by);
                                state.MotionX[chroma] = (sbyte)Round(sumX, hs * vs); state.MotionY[chroma] = (sbyte)Round(sumY, hs * vs);
                            }
                        }
                    }
                    continue;
                }
                switch (mode)
                {
                    case 2:
                        x = ReadMotionComponent(ref reader, fixedLength); y = ReadMotionComponent(ref reader, fixedLength);
                        priorX = lastX; priorY = lastY; lastX = x; lastY = y; break;
                    case 3: x = lastX; y = lastY; break;
                    case 4: x = priorX; y = priorY; priorX = lastX; priorY = lastY; lastX = x; lastY = y; break;
                    case 6: x = ReadMotionComponent(ref reader, fixedLength); y = ReadMotionComponent(ref reader, fixedLength); break;
                    case 0: case 1: case 5: break;
                    default: throw new InvalidDataException("Invalid Theora macroblock mode.");
                }
                for (int plane = 0; plane < 3; plane++)
                {
                    int hs = plane == 0 ? 1 : layout.Info.HorizontalSubsampling, vs = plane == 0 ? 1 : layout.Info.VerticalSubsampling;
                    for (int by = 0; by < 2 / vs; by++)
                    {
                        for (int bx = 0; bx < 2 / hs; bx++)
                        {
                            int block = layout.Block(plane, mx * (2 / hs) + bx, my * (2 / vs) + by);
                            state.MotionX[block] = (sbyte)x; state.MotionY[block] = (sbyte)y;
                        }
                    }
                }
            }
        }

        private static int Round(int value, int divisor) => Math.Sign(value) * ((Math.Abs(value) + divisor / 2) / divisor);

        private static int ReadMotionComponent(ref TheoraBitReader reader, bool fixedLength)
        {
            int magnitude;
            if (fixedLength) { magnitude = reader.Read(5); }
            else
            {
                int prefix = reader.Read(3);
                switch (prefix)
                {
                    case 0: return 0;
                    case 1: return 1;
                    case 2: return -1;
                    case 3: magnitude = 2; break;
                    case 4: magnitude = 3; break;
                    case 5: magnitude = 4 + reader.Read(2); break;
                    case 6: magnitude = 8 + reader.Read(3); break;
                    case 7: magnitude = 16 + reader.Read(4); break;
                    default: throw new InvalidDataException("Invalid Theora motion code.");
                }
            }
            return reader.Flag() ? -magnitude : magnitude;
        }
    }
}

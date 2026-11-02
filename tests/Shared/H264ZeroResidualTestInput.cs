// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class H264ZeroResidualTestInput
    {
        public static void VerifyMacroblocks()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            for (int qp = 0; qp <= 51; qp++)
            {
                foreach ((int x, int y) in new[] { (0, 0), (16, 0), (0, 16), (16, 16) })
                {
                    using H264Picture picture = new(new() { WidthInMbs = 2, HeightInMapUnits = 2, FrameMbsOnly = true, Width = 32, Height = 32 }, allocator, H264SliceKind.P);
                    byte[][] expected = new byte[3][];
                    byte[][] counts = new byte[3][];
                    for (int plane = 0; plane < 3; plane++)
                    {
                        picture.Plane(plane).Fill((byte)(plane * 31 + 9));
                        picture.Counts(plane).Fill(17);
                        expected[plane] = picture.Plane(plane).ToArray();
                        counts[plane] = picture.Counts(plane).ToArray();
                    }
                    for (int index = 0; index < picture.Modes.Length; index++)
                    {
                        picture.Modes[index] = (byte)(128 + index % 10);
                    }
                    byte[] modes = picture.Modes.ToArray();
                    byte[] prediction = new byte[384];
                    for (int index = 0; index < prediction.Length; index++)
                    {
                        prediction[index] = (byte)((index * 53 + qp * 97) & 255);
                    }
                    byte[] original = prediction.ToArray();
                    for (int plane = 0; plane < 3; plane++)
                    {
                        int scale = plane == 0 ? 1 : 2;
                        int size = 16 / scale;
                        int stride = 32 / scale;
                        int start = plane switch { 0 => 0, 1 => 256, 2 => 320, _ => throw new InvalidOperationException() };
                        for (int row = 0; row < size; row++)
                        {
                            for (int column = 0; column < size; column++)
                            {
                                expected[plane][(y / scale + row) * stride + x / scale + column] = prediction[start + row * size + column];
                            }
                        }
                        for (int row = 0; row < size / 4; row++)
                        {
                            for (int column = 0; column < size / 4; column++)
                            {
                                int index = (y / scale / 4 + row) * (stride / 4) + x / scale / 4 + column;
                                counts[plane][index] = 0;
                                if (plane == 0)
                                {
                                    modes[index] &= 127;
                                }
                            }
                        }
                    }
                    H264BitReader bits = new([]);
                    H264Residuals.ReadInterLuma(ref bits, picture, x, y, 0, qp, prediction.AsSpan(0, 256));
                    H264Residuals.ReadChroma(ref bits, picture, new(0, 0, false, false, 26, -12, 12, true, false, false, false, 1, 1, false, 0),
                        x / 2, y / 2, 0, qp, prediction.AsSpan(256));
                    for (int plane = 0; plane < 3; plane++)
                    {
                        if (!picture.Plane(plane).SequenceEqual(expected[plane]) || !picture.Counts(plane).SequenceEqual(counts[plane]))
                        {
                            throw new InvalidDataException("Zero residual reconstruction changed pixels or coefficient counts outside its macroblock.");
                        }
                    }
                    if (!picture.Modes.SequenceEqual(modes) || !prediction.AsSpan().SequenceEqual(original) || bits.BitPosition != 0)
                    {
                        throw new InvalidDataException("Zero residual reconstruction changed modes, prediction ownership or the bitstream cursor.");
                    }
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("Zero residual reconstruction retained picture storage.");
            }
        }
    }
}

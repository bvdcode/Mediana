// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using Mediana.Codecs.H264;
using Mediana.Core.Memory;
using Mediana.Formats.Video;

namespace Mediana.Tests
{
    internal static class H264ResidualRegionTestInput
    {
        public static int Verify()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            int cases = 0;
            for (int qp = 0; qp <= 51; qp++)
            {
                for (int pattern = 0; pattern < 16; pattern++)
                {
                    for (int address = 0; address < 9; address++)
                    {
                        for (int style = 0; style < 3; style++)
                        {
                            VerifyCase(allocator, qp, pattern, address, style);
                            cases++;
                        }
                    }
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidDataException("Residual reconstruction retained owned media.");
            }
            return cases;
        }

        private static void VerifyCase(MediaBufferAllocator allocator, int qp, int pattern, int address, int style)
        {
            using H264Picture picture = new(new() { WidthInMbs = 3, HeightInMapUnits = 3, FrameMbsOnly = true, Width = 48, Height = 48 }, allocator, H264SliceKind.P);
            picture.Plane(0).Fill(37);
            picture.Plane(1).Fill(73);
            picture.Plane(2).Fill(91);
            for (int plane = 0; plane < 3; plane++)
            {
                picture.Counts(plane).Fill(17);
            }
            for (int index = 0; index < picture.Modes.Length; index++)
            {
                picture.Modes[index] = (byte)(128 + index % 10);
            }
            byte[] expectedPixels = picture.Plane(0).ToArray();
            byte[] expectedCounts = picture.Counts(0).ToArray();
            byte[] expectedModes = picture.Modes.ToArray();
            byte[] prediction = new byte[256];
            for (int index = 0; index < prediction.Length; index++)
            {
                prediction[index] = (byte)((index * 53 + qp * 97) & 255);
            }
            byte[] original = prediction.ToArray();
            int x = address % 3 * 16;
            int y = address / 3 * 16;
            ReadOnlySpan<int> scales = [10, 11, 13, 14, 16, 18];
            for (int index = 0; index < 16; index++)
            {
                (int dx, int dy) = H264ResidualRegionReader.Position(index);
                int count = (pattern & (1 << (index / 4))) == 0 ? 0 : H264ResidualRegionReader.Count(index, style);
                int residual = count == 0 ? 0 : (H264ResidualRegionReader.Level(index) * scales[qp % 6] * (1 << (qp / 6)) + 32) >> 6;
                int position = (y + dy) / 4 * 12 + (x + dx) / 4;
                expectedCounts[position] = (byte)count;
                expectedModes[position] &= 127;
                for (int row = 0; row < 4; row++)
                {
                    for (int column = 0; column < 4; column++)
                    {
                        expectedPixels[(y + dy + row) * 48 + x + dx + column] =
                            (byte)Math.Clamp(prediction[(dy + row) * 16 + dx + column] + residual, 0, 255);
                    }
                }
            }
            H264ResidualRegionReader reader = new(picture, x, y, pattern, style);
            H264BitReader bits = new([255, 255, 255]);
            H264Residuals.ReadInterLuma(ref bits, reader, picture, x, y, pattern, qp, prediction);
            if (!picture.Plane(0).SequenceEqual(expectedPixels) || !picture.Counts(0).SequenceEqual(expectedCounts)
                || !picture.Modes.SequenceEqual(expectedModes) || !prediction.AsSpan().SequenceEqual(original)
                || picture.Plane(1).ContainsAnyExcept((byte)73) || picture.Plane(2).ContainsAnyExcept((byte)91)
                || picture.Counts(1).ContainsAnyExcept((byte)17) || picture.Counts(2).ContainsAnyExcept((byte)17)
                || reader.Calls != BitOperations.PopCount((uint)pattern) * 4 || bits.BitPosition != reader.Calls)
            {
                throw new InvalidDataException("Residual reconstruction differs from independent pixels, counts, modes, bounds or input cursor.");
            }
        }
    }
}

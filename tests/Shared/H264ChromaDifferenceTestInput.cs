// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;
using Mediana.Formats.Video;

namespace Mediana.Tests
{
    internal static class H264ChromaDifferenceTestInput
    {
        public static int Verify()
        {
            Random random = new(23891);
            MediaBufferAllocator allocator = new(1024 * 1024);
            int checks = 0;
            foreach ((int width, int height) in new[] { (16, 16), (32, 16), (16, 32), (48, 32), (46, 30) })
            {
                H264SequenceParameterSet sequence = new()
                {
                    WidthInMbs = (width + 15) / 16,
                    HeightInMapUnits = (height + 15) / 16,
                    FrameMbsOnly = true,
                    Width = width,
                    Height = height
                };
                using H264Picture picture = new(sequence, allocator, H264SliceKind.P);
                random.NextBytes(picture.Plane(1));
                random.NextBytes(picture.Plane(2));
                checks += VerifyVectors(picture, random);
                checks += VerifyExtremes(picture);
                VerifyAllocations(picture);
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("The chroma difference checks retained picture storage.");
            }
            return checks;
        }

        private static int VerifyVectors(H264Picture picture, Random random)
        {
            byte[] expected = new byte[128];
            byte[] materialized = new byte[128];
            byte[] source = new byte[128];
            int[] integers = [int.MinValue, -128, -16, -8, 0, 8, 128, int.MaxValue - 7];
            int checks = 0;
            foreach ((int x, int y) in new[]
            {
                (0, 0), (picture.Width - 16, 0), (0, picture.Height - 16), (picture.Width - 16, picture.Height - 16)
            })
            {
                foreach (int integerX in integers)
                {
                    foreach (int integerY in integers)
                    {
                        for (int dy = 0; dy < 8; dy++)
                        {
                            for (int dx = 0; dx < 8; dx++)
                            {
                                int motionX = integerX + dx;
                                int motionY = integerY + dy;
                                Predict(picture, x, y, motionX, motionY, expected);
                                H264PredictionBlock.Read(picture, 1, x, y, 16, 16, motionX, motionY, materialized.AsSpan(0, 64));
                                H264PredictionBlock.Read(picture, 2, x, y, 16, 16, motionX, motionY, materialized.AsSpan(64));
                                if (!expected.AsSpan().SequenceEqual(materialized))
                                {
                                    throw new InvalidDataException("Chroma prediction differs from the bilinear reference.");
                                }
                                random.NextBytes(source);
                                checks += Compare(picture, x, y, motionX, motionY, source, expected);
                                expected.CopyTo(source, 0);
                                checks += Compare(picture, x, y, motionX, motionY, source, expected);
                            }
                        }
                    }
                }
            }
            return checks;
        }

        private static int VerifyExtremes(H264Picture picture)
        {
            byte[] expected = new byte[128];
            byte[] source = new byte[128];
            int checks = 0;
            for (int value = 0; value <= byte.MaxValue; value += byte.MaxValue)
            {
                picture.Plane(1).Fill((byte)value);
                picture.Plane(2).Fill((byte)value);
                expected.AsSpan().Fill((byte)value);
                source.AsSpan().Fill((byte)(byte.MaxValue - value));
                for (int dy = 0; dy < 8; dy++)
                {
                    for (int dx = 0; dx < 8; dx++)
                    {
                        checks += Compare(picture, 0, 0, dx, dy, source, expected);
                        checks += Compare(picture, picture.Width - 16, picture.Height - 16, dx - 8, dy - 8, source, expected);
                    }
                }
            }
            return checks;
        }

        private static int Compare(H264Picture picture, int x, int y, int motionX, int motionY,
            ReadOnlySpan<byte> source, ReadOnlySpan<byte> expected)
        {
            int complete = 0;
            for (int index = 0; index < source.Length; index++)
            {
                complete += Math.Abs(source[index] - expected[index]);
            }
            VerifyCost(picture, x, y, motionX, motionY, source, complete, 0);
            VerifyCost(picture, x, y, motionX, motionY, source, complete, complete);
            VerifyCost(picture, x, y, motionX, motionY, source, complete, Math.Max(0, complete - 1));
            VerifyCost(picture, x, y, motionX, motionY, source, complete, int.MaxValue);
            return 4;
        }

        private static void VerifyCost(H264Picture picture, int x, int y, int motionX, int motionY,
            ReadOnlySpan<byte> source, int complete, int cutoff)
        {
            int actual = H264ChromaDifference.Sum8x8(picture, x, y, motionX, motionY, source, cutoff);
            if ((complete <= cutoff && actual != complete) || (complete > cutoff && (actual <= cutoff || actual > complete)))
            {
                throw new InvalidDataException($"Chroma cost differs for MV {motionX},{motionY}: {actual}; full {complete}; cutoff {cutoff}.");
            }
        }

        private static void Predict(H264Picture picture, int x, int y, int motionX, int motionY, Span<byte> output)
        {
            int stride = picture.Width / 2;
            int rows = picture.Height / 2;
            long startX = (long)x / 2 + (motionX >> 3);
            long startY = (long)y / 2 + (motionY >> 3);
            int dx = motionX & 7;
            int dy = motionY & 7;
            for (int plane = 1; plane < 3; plane++)
            {
                ReadOnlySpan<byte> samples = picture.ReadPlane(plane);
                for (int row = 0; row < 8; row++)
                {
                    for (int column = 0; column < 8; column++)
                    {
                        int left = (int)Math.Clamp(startX + column, 0, stride - 1);
                        int right = (int)Math.Clamp(startX + column + 1, 0, stride - 1);
                        int top = (int)Math.Clamp(startY + row, 0, rows - 1);
                        int bottom = (int)Math.Clamp(startY + row + 1, 0, rows - 1);
                        int value = (8 - dx) * (8 - dy) * samples[top * stride + left]
                            + dx * (8 - dy) * samples[top * stride + right]
                            + (8 - dx) * dy * samples[bottom * stride + left]
                            + dx * dy * samples[bottom * stride + right];
                        output[(plane - 1) * 64 + row * 8 + column] = (byte)((value + 32) >> 6);
                    }
                }
            }
        }

        private static void VerifyAllocations(H264Picture picture)
        {
            byte[] source = new byte[128];
            source.AsSpan().Fill(93);
            int expected = Repeat(picture, source);
            for (int warmup = 0; warmup < 4; warmup++)
            {
                if (Repeat(picture, source) != expected)
                {
                    throw new InvalidOperationException("Chroma cost changed during repeated evaluation.");
                }
            }
            _ = GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            int actual = Repeat(picture, source);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (actual != expected || allocated != 0)
            {
                throw new InvalidOperationException($"Repeated chroma evaluation allocated {allocated} bytes or changed its result.");
            }
        }

        private static int Repeat(H264Picture picture, ReadOnlySpan<byte> source)
        {
            int checksum = 0;
            for (int iteration = 0; iteration < 32; iteration++)
            {
                for (int phase = 0; phase < 64; phase++)
                {
                    int motionX = (phase & 7) - (iteration & 1) * 8;
                    int motionY = (phase >> 3) - (iteration & 1) * 8;
                    int x = (iteration & 2) == 0 ? 0 : picture.Width - 16;
                    int y = (iteration & 4) == 0 ? 0 : picture.Height - 16;
                    checksum += H264ChromaDifference.Sum8x8(picture, x, y, motionX, motionY, source, int.MaxValue);
                }
            }
            return checksum;
        }
    }
}

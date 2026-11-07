// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Webp;
using Mediana.Core.Memory;

using Mediana.Codecs.Vp8;

namespace Mediana.Tests
{
    public class Vp8IntraPredictionTests
    {
        public static IEnumerable<object[]> Predictions =>
        [
            [0, new byte[] { 75, 75, 75, 75, 75, 75, 75, 75, 75, 75, 75, 75, 75, 75, 75, 75 }],
            [1, new byte[] { 40, 60, 100, 120, 80, 100, 140, 160, 120, 140, 180, 200, 160, 180, 220, 240 }],
            [2, new byte[] { 23, 45, 75, 100, 23, 45, 75, 100, 23, 45, 75, 100, 23, 45, 75, 100 }],
            [3, new byte[] { 35, 35, 35, 35, 70, 70, 70, 70, 110, 110, 110, 110, 140, 140, 140, 140 }],
            [4, new byte[] { 45, 75, 100, 125, 75, 100, 125, 160, 100, 125, 160, 200, 125, 160, 200, 230 }],
            [5, new byte[] { 18, 23, 45, 75, 35, 18, 23, 45, 70, 35, 18, 23, 110, 70, 35, 18 }],
            [6, new byte[] { 15, 30, 60, 90, 18, 23, 45, 75, 35, 15, 30, 60, 70, 18, 23, 45 }],
            [7, new byte[] { 30, 60, 90, 110, 45, 75, 100, 125, 60, 90, 110, 160, 75, 100, 125, 200 }],
            [8, new byte[] { 20, 18, 23, 45, 50, 35, 20, 18, 90, 70, 50, 35, 130, 110, 90, 70 }],
            [9, new byte[] { 50, 70, 90, 110, 90, 110, 130, 140, 130, 140, 150, 150, 150, 150, 150, 150 }]
        ];

        [Theory]
        [MemberData(nameof(Predictions))]
        public void DirectionalPredictionMatchesNormativePixels(int mode, byte[] expected)
        {
            Span<byte> prediction = stackalloc byte[16];
            Vp8IntraPrediction.Predict((Vp8IntraMode)mode, [20, 40, 80, 100, 120, 160, 200, 240], [30, 70, 110, 150], 10, prediction);
            Assert.Equal(expected, prediction.ToArray());
        }

        [Fact]
        public void EveryModeRoundTripsEveryNeighborContext()
        {
            ReadOnlySpan<sbyte> tree = [0, 2, -1, 4, -2, 6, 8, 12, -3, 10, -5, -6, -4, 14, -7, 16, -8, -9];
            MediaBufferAllocator allocator = new(1024 * 1024);
            using WebpByteBuffer buffer = new(allocator);
            Vp8BooleanWriter writer = new(buffer);
            for (int above = 0; above < 10; above++)
            {
                for (int left = 0; left < 10; left++)
                {
                    for (int mode = 0; mode < 10; mode++) { Vp8IntraModeEncoder.Write(writer, (Vp8IntraMode)mode, above, left); }
                }
            }
            writer.Finish();
            byte[] encoded = new byte[buffer.Length];
            buffer.CopyTo(encoded);
            Vp8TestBooleanReader reader = new(encoded);
            for (int above = 0; above < 10; above++)
            {
                for (int left = 0; left < 10; left++)
                {
                    for (int mode = 0; mode < 10; mode++)
                    {
                        int node = 0;
                        do { node = tree[node + (reader.Read(Vp8ProbabilityTables.BlockModes[(above * 10 + left) * 9 + node / 2]) ? 1 : 0)]; } while (node > 0);
                        Assert.Equal(mode, -node);
                    }
                }
            }
        }

        [Fact]
        public void SimdErrorRetainsFullRangeAndMatchesIntegerArithmetic()
        {
            byte[] source = new byte[16];
            byte[] prediction = Enumerable.Repeat((byte)255, 16).ToArray();
            Assert.Equal(1040400, Vp8IntraPrediction.SquaredError(source, prediction));
            Random random = new(73112);
            for (int iteration = 0; iteration < 1000; iteration++)
            {
                random.NextBytes(source);
                random.NextBytes(prediction);
                int expected = source.Zip(prediction, (a, b) => (a - b) * (a - b)).Sum();
                Assert.Equal(expected, Vp8IntraPrediction.SquaredError(source, prediction));
            }
        }

        [Fact]
        public void RowCachePreservesCornerAndUpperRightPixels()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using Vp8Reconstruction reconstruction = new(32, allocator);
            Span<byte> plane = reconstruction.Plane(0);
            for (int x = 0; x < 32; x++) { plane[15 * 32 + x] = (byte)(x + 30); }
            Span<byte> top = stackalloc byte[8];
            Span<byte> left = stackalloc byte[4];
            reconstruction.BeginMacroblock(0, false);
            plane[15 * 32 + 15] = 200;
            reconstruction.BeginMacroblock(16, false);
            Assert.Equal(45, reconstruction.LumaNeighbors(16, 0, false, top, left));
            Assert.Equal(new byte[] { 46, 47, 48, 49, 50, 51, 52, 53 }, top.ToArray());
            reconstruction.LumaNeighbors(28, 12, false, top, left);
            Assert.Equal(new byte[] { 61, 61, 61, 61 }, top[4..].ToArray());
            reconstruction.BeginMacroblock(0, true);
            Assert.Equal(127, reconstruction.LumaNeighbors(0, 0, true, top, left));
            Assert.All(top.ToArray(), value => Assert.Equal(127, value));
            Assert.All(left.ToArray(), value => Assert.Equal(129, value));
            reconstruction.LumaNeighbors(12, 12, true, top, left);
            Assert.All(top[4..].ToArray(), value => Assert.Equal(127, value));
        }

        [Theory]
        [InlineData(false, false, 128)]
        [InlineData(true, false, 20)]
        [InlineData(false, true, 80)]
        [InlineData(true, true, 50)]
        public void ChromaDcAveragesOnlyAvailableNeighbors(bool hasTop, bool hasLeft, int expected)
        {
            Span<byte> prediction = stackalloc byte[64];
            byte[] top = Enumerable.Repeat((byte)20, 8).ToArray();
            byte[] left = Enumerable.Repeat((byte)80, 8).ToArray();
            Vp8ChromaPrediction.Predict(Vp8ChromaMode.Dc, top, left, 127, hasTop, hasLeft, prediction);
            Assert.All(prediction.ToArray(), value => Assert.Equal(expected, value));
        }
    }
}

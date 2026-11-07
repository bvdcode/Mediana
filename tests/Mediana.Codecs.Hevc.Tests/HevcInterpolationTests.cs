// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.Codecs.Hevc.Tests
{
    public class HevcInterpolationTests
    {
        [Fact]
        public void AllFractionalPhasesAndBordersMatchIndependentMainAndMain10Predictions()
        {
            Assert.Equal(1280, HevcInterpolationTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data")));
        }

        [Fact]
        public void RepeatedInterpolationDoesNotAllocateManagedStorage()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data");
            HevcInterpolationTestInput.Verify(root);
            Assert.Equal(1280, HevcInterpolationTestInput.Verify(root, true));
        }

        [Fact]
        public void InterpolationCannotOverwriteItsBorrowedReference()
        {
            byte[] source = new byte[256]; int[] output = new int[16];
            Assert.Throws<ArgumentException>(() => HevcInterpolation.Predict<byte, ByteSampleFormat>(
                new(source, 8, 8), 0, 0, 1, 1, false, 8, 4, 4, output,
                System.Runtime.InteropServices.MemoryMarshal.Cast<byte, int>(source.AsSpan())));
        }
    }
}

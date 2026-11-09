// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1SuperResolutionFilterTests
    {
        [Fact]
        public void MatchesIndependentConvolutionAcrossDenominatorsDepthsAndPaddedEdges() =>
            Av1SuperResolutionKernelTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data"));

        [Fact]
        public void RejectsOverlappingOutputAndInvalidGeometry()
        {
            byte[] source = new byte[64], output = new byte[64];
            Assert.Throws<ArgumentException>(() => Av1SuperResolutionFilter.Row(source, 32, source));
            Assert.Throws<ArgumentOutOfRangeException>(() => Av1SuperResolutionFilter.Row(source, 0, output));
            Assert.Throws<ArgumentOutOfRangeException>(() => Av1SuperResolutionFilter.Row(source, 65, output));
            Assert.Throws<ArgumentOutOfRangeException>(() => Av1SuperResolutionFilter.Row(source, 32, output.AsSpan(0, 31)));
            Assert.Throws<ArgumentOutOfRangeException>(() => Av1SuperResolutionFilter.Row(new ushort[64], 32, new ushort[64], 9));
        }

        [Fact]
        public void RepeatedRowsAllocateNoManagedStorage()
        {
            byte[] source = Enumerable.Range(0, 64).Select(value => (byte)(value * 3)).ToArray();
            byte[] output = new byte[64];
            Av1SuperResolutionFilter.Row(source, 48, output);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 100; index++) { Av1SuperResolutionFilter.Row(source, 48, output); }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.Codecs.Opus.Tests
{
    public class SilkParameterReconstructionTests
    {
        [Fact]
        public void InterpolationGainHistoryAndBothFilterSetsMatchIndependentReconstruction() =>
            Assert.Equal(2304, OpusSilkParametersTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data")));

        [Theory]
        [InlineData(8000)]
        [InlineData(12000)]
        [InlineData(16000)]
        public void RepeatedParametersReuseCallerStorage(int sampleRate)
        {
            SilkFrameMode mode = new(sampleRate, 4);
            Span<byte> gainIndices = stackalloc byte[4], filters = stackalloc byte[4];
            Span<sbyte> lsf = stackalloc sbyte[17];
            Span<short> previousLsf = stackalloc short[16], lpc = stackalloc short[32], ltp = stackalloc short[20];
            Span<int> gains = stackalloc int[4], lags = stackalloc int[4];
            gainIndices.Fill(4); filters.Clear(); lsf.Clear(); previousLsf.Clear();
            SilkFrameIndices indices = new(SilkSignalType.Voiced, 0, 2, 1, default);
            int previousGain = 0;
            for (int at = 0; at < 2; at++)
            {
                SilkParameterReconstruction.Reconstruct(mode, SilkCodingMode.Conditional, indices, new(gainIndices, lsf, filters),
                    new(gains, lpc, lags, ltp), previousLsf, ref previousGain, at == 0);
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int at = 0; at < 1000; at++)
            {
                SilkParameterReconstruction.Reconstruct(mode, SilkCodingMode.Conditional, indices, new(gainIndices, lsf, filters),
                    new(gains, lpc, lags, ltp), previousLsf, ref previousGain, false);
            }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }
}

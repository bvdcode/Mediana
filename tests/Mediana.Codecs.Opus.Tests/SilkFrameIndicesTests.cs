// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.Codecs.Opus.Tests
{
    public class SilkFrameIndicesTests
    {
        [Fact]
        public void CompleteIndicesAndPulsesMatchTheIndependentDecoder() =>
            Assert.Equal(3456, OpusSilkIndicesTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data")));

        [Fact]
        public void RepeatedFrameSyntaxAndPitchReconstructionReuseCallerStorage()
        {
            ReadOnlySpan<byte> bytes = [105, 12, 195, 52, 0, 255, 102, 194, 250];
            Span<byte> gains = stackalloc byte[4], filters = stackalloc byte[4];
            Span<sbyte> lsf = stackalloc sbyte[17];
            Span<short> pulses = stackalloc short[320], coefficients = stackalloc short[20];
            Span<int> lags = stackalloc int[4];
            for (int at = 0; at < 2; at++) { Decode(bytes, new(gains, lsf, filters), pulses, coefficients, lags); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int at = 0; at < 1000; at++) { Decode(bytes, new(gains, lsf, filters), pulses, coefficients, lags); }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        [Theory]
        [InlineData(-32768, 32)]
        [InlineData(32767, 288)]
        public void PitchContoursClampAtTheNormativeLagBounds(short primary, int expected)
        {
            Span<int> lags = stackalloc int[4];
            Span<short> coefficients = stackalloc short[20];
            SilkPitchReader.Dequantize(new(16000, 4), new(primary, 0, 0, 0), new byte[4], lags, coefficients);
            foreach (int lag in lags) { Assert.Equal(expected, lag); }
        }

        private static void Decode(ReadOnlySpan<byte> bytes, SilkIndexBuffers buffers, Span<short> pulses, Span<short> coefficients, Span<int> lags)
        {
            OpusRangeDecoder decoder = new(bytes);
            SilkFrameMode mode = new(16000, 4);
            SilkEntropyHistory history = new(SilkSignalType.Voiced, 17);
            SilkFrameIndices indices = SilkFrameIndicesReader.Read(ref decoder, mode, SilkCodingMode.Conditional, true, ref history, buffers);
            SilkPulseReader.Read(ref decoder, pulses, mode.FrameSamples, indices.Signal, indices.QuantizationOffset);
            if (indices.Signal == SilkSignalType.Voiced) { SilkPitchReader.Dequantize(mode, indices.Pitch, buffers.PitchFilters, lags, coefficients); }
        }
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.Codecs.Opus.Tests
{
    public class SilkControlsTests
    {
        [Fact]
        public void GainsStereoPredictionAndExcitationMatchIndependentDecoding() =>
            Assert.Equal(1536, OpusSilkControlsTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data")));

        [Fact]
        public void RepeatedControlsAndExcitationReuseCallerStorage()
        {
            ReadOnlySpan<byte> bytes = [15, 29, 192, 65, 100, 84, 0, 255];
            Span<byte> indices = stackalloc byte[4];
            Span<int> gains = stackalloc int[4], excitation = stackalloc int[320];
            Span<short> pulses = stackalloc short[320];
            pulses.Fill(1);
            for (int at = 0; at < 2; at++) { Decode(bytes, indices, gains, excitation, pulses); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int at = 0; at < 1000; at++) { Decode(bytes, indices, gains, excitation, pulses); }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        [Fact]
        public void InvalidGainIndicesPreserveDestinationAndHistory()
        {
            int previous = 31;
            int[] gains = [17, 17, 17, 17];
            Assert.Throws<ArgumentOutOfRangeException>(() => SilkGainReader.Dequantize(new byte[] { 31, 4, 4, 41 }, gains, ref previous, false));
            Assert.Equal(31, previous);
            Assert.All(gains, value => Assert.Equal(17, value));
        }

        [Theory]
        [InlineData(0, 0, -1)]
        [InlineData(0, 0, 4)]
        [InlineData(3, 0, 1)]
        [InlineData(0, 2, 1)]
        public void InvalidExcitationParametersPreserveTheDestination(int signal, int offset, int seed)
        {
            int[] excitation = Enumerable.Repeat(17, 80).ToArray();
            Assert.Throws<ArgumentOutOfRangeException>(() => SilkExcitation.Reconstruct(new short[80], excitation, (SilkSignalType)signal, offset, seed));
            Assert.All(excitation, value => Assert.Equal(17, value));
        }

        private static void Decode(ReadOnlySpan<byte> bytes, Span<byte> indices, Span<int> gains, Span<int> excitation, ReadOnlySpan<short> pulses)
        {
            OpusRangeDecoder decoder = new(bytes);
            SilkGainReader.Read(ref decoder, indices, 4, SilkSignalType.Voiced, false);
            int previous = 0;
            SilkGainReader.Dequantize(indices, gains, ref previous, false);
            SilkStereoReader.ReadPrediction(ref decoder); SilkStereoReader.ReadMidOnly(ref decoder);
            SilkExcitation.Reconstruct(pulses, excitation, SilkSignalType.Voiced, 0, 1);
        }
    }
}

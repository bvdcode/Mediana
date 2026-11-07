// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.Codecs.Opus.Tests
{
    public class SilkPulseReaderTests
    {
        [Fact]
        public void ShellLocationsExtraBitsSignsAndPaddingMatchTheIndependentDecoder() =>
            Assert.Equal(2880, OpusSilkPulseTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data")));

        [Fact]
        public void RepeatedPulseDecodingAllocatesNoManagedStorage()
        {
            ReadOnlySpan<byte> bytes = [0, 17, 192, 12, 92, 56, 144, 2, 99, 255];
            Span<short> pulses = stackalloc short[320];
            OpusRangeDecoder initial = new(bytes);
            SilkPulseReader.Read(ref initial, pulses, 320, SilkSignalType.Voiced, 1);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int at = 0; at < 1000; at++)
            {
                OpusRangeDecoder decoder = new(bytes);
                SilkPulseReader.Read(ref decoder, pulses, 320, SilkSignalType.Voiced, 1);
            }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        [Theory]
        [InlineData(119, 0, 0, 320)]
        [InlineData(120, 0, 0, 120)]
        [InlineData(320, 3, 0, 320)]
        [InlineData(320, 2, 2, 320)]
        public void InvalidParametersPreserveEntropyAndDestination(int samples, int signal, int offset, int length)
        {
            short[] pulses = Enumerable.Repeat((short)17, 320).ToArray();
            Assert.Throws<ArgumentOutOfRangeException>(() => ReadRejected(pulses, samples, signal, offset, length));
            Assert.All(pulses, value => Assert.Equal(17, value));
        }

        private static void ReadRejected(short[] pulses, int samples, int signal, int offset, int length)
        {
            OpusRangeDecoder decoder = new(new byte[10]);
            uint initial = decoder.Range;
            int bits = decoder.BitsUsed;
            try { SilkPulseReader.Read(ref decoder, pulses.AsSpan(0, length), samples, (SilkSignalType)signal, offset); }
            finally { Assert.Equal(initial, decoder.Range); Assert.Equal(bits, decoder.BitsUsed); }
        }
    }
}

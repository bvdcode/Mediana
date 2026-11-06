// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.Codecs.Opus.Tests
{
    public class CeltFrameSyntaxTests
    {
        [Fact]
        public void SequentialFrameSyntaxMatchesTheIndependentDecoderBeforeShapeReconstruction() =>
            Assert.Equal(1024, OpusFrameSyntaxTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data")));

        [Fact]
        public void SilenceConsumesItsBudgetWithoutInventingFurtherSymbols()
        {
            OpusRangeDecoder decoder = new(new byte[] { 255, 255 });
            Span<float> energies = stackalloc float[21];
            Span<int> tf = stackalloc int[21], boosts = stackalloc int[21], shape = stackalloc int[21], fine = stackalloc int[21];
            Span<byte> priority = stackalloc byte[21];
            energies.Clear();
            CeltFrameSyntax syntax = CeltFrameSyntaxReader.Read(ref decoder, new(1, 3, 0, 21), energies,
                tf, boosts, new(shape, fine, priority));
            Assert.True(syntax.Flags.IsSilent);
            Assert.False(syntax.Flags.IsTransient);
            Assert.Equal(16, decoder.BitsUsed);
            Assert.Equal(128, decoder.EighthBitsUsed);
            foreach (int boost in boosts) { Assert.Equal(0, boost); }
            foreach (int bits in shape) { Assert.Equal(0, bits); }
            foreach (int bits in fine) { Assert.Equal(0, bits); }
        }

        [Fact]
        public void RepeatedFrameSyntaxReadingUsesNoManagedStorage()
        {
            byte[] bytes = new byte[32];
            Span<float> energies = stackalloc float[42];
            Span<int> tf = stackalloc int[21], boosts = stackalloc int[21], shape = stackalloc int[21], fine = stackalloc int[21];
            Span<byte> priority = stackalloc byte[21];
            energies.Clear();
            CeltBandMode mode = new(2, 3, 0, 21);
            OpusRangeDecoder warm = new(bytes);
            CeltFrameSyntaxReader.Read(ref warm, mode, energies, tf, boosts, new(shape, fine, priority));
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 1000; index++)
            {
                OpusRangeDecoder decoder = new(bytes);
                CeltFrameSyntaxReader.Read(ref decoder, mode, energies, tf, boosts, new(shape, fine, priority));
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, allocated);
        }
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Ac3;
using Mediana.Core.Memory;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Ac3.Tests
{
    public class Ac3SyntaxTests
    {
        [Theory]
        [InlineData(false, true, 6, "switched.ac3")]
        [InlineData(true, true, 6, "enhanced-switched.eac3")]
        [InlineData(true, false, 1, "enhanced-one.eac3")]
        [InlineData(true, false, 2, "enhanced-two.eac3")]
        [InlineData(true, false, 3, "enhanced-three.eac3")]
        public void NormativeFixtureIsReproducible(bool enhanced, bool switched, int blocks, string file)
        {
            byte[] actual = Ac3SyntheticFrame.Create(enhanced, switched, blocks);
            Assert.Equal(File.ReadAllBytes(Ac3DecoderTests.Data(file)), actual);
        }

        [Theory]
        [InlineData(false, 2, "d25.ac3")]
        [InlineData(false, 3, "d45.ac3")]
        [InlineData(true, 2, "enhanced-d25.eac3")]
        [InlineData(true, 3, "enhanced-d45.eac3")]
        public void ReproducesGroupedExponentFixtures(bool enhanced, int strategy, string file)
        {
            Assert.Equal(File.ReadAllBytes(Ac3DecoderTests.Data(file)), Ac3SyntheticFrame.Create(enhanced, false, exponentStrategy: strategy));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ReusesTheReservedSynthesisAndSpectralStorage(bool enhanced)
        {
            byte[] bytes = Ac3SyntheticFrame.Create(enhanced, true);
            Ac3FrameHeader header = Ac3FrameHeader.Read(bytes);
            float[] pcm = new float[1536];
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (Ac3FrameDecoder decoder = new(allocator))
            {
                for (int i = 0; i < 5; i++) { decoder.Decode(bytes, header, pcm, CancellationToken.None); }
                long reserved = allocator.AllocatedBytes, before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 128; i++) { decoder.Decode(bytes, header, pcm, CancellationToken.None); }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.Equal(0, allocated); Assert.Equal(reserved, allocator.AllocatedBytes);
                Assert.Equal(32328, reserved);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AppliesOrDisablesDynamicRangeAtTheSpectralBlockBoundary(bool enhanced)
        {
            byte[] input = Ac3SyntheticFrame.Create(enhanced, true, dynamicRange: 0x20);
            Ac3FrameHeader header = Ac3FrameHeader.Read(input);
            MediaBufferAllocator allocator = new(1024 * 1024);
            float[] compressed = new float[1536], fullRange = new float[1536];
            using (Ac3FrameDecoder decoder = new(allocator, true)) { decoder.Decode(input, header, compressed, CancellationToken.None); }
            using (Ac3FrameDecoder decoder = new(allocator, false)) { decoder.Decode(input, header, fullRange, CancellationToken.None); }
            Assert.Contains(fullRange, value => Math.Abs(value) > 0.0001f);
            for (int i = 0; i < fullRange.Length; i++) { Assert.Equal(2 * fullRange[i], compressed[i]); }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}

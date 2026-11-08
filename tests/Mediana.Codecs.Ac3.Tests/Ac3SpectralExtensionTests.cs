// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Ac3.Tests
{
    public class Ac3SpectralExtensionTests
    {
        [Theory]
        [InlineData(-1, false, 31, "spectral.eac3")]
        [InlineData(0, false, 31, "spectral-atten0.eac3")]
        [InlineData(31, false, 31, "spectral-atten31.eac3")]
        [InlineData(-1, true, 31, "spectral-merged.eac3")]
        [InlineData(-1, false, 0, "spectral-noise.eac3")]
        public void ReproducesIndependentlyDecodedSpectralExtensionVectors(int attenuation, bool merged, int blend, string name)
        {
            Assert.Equal(File.ReadAllBytes(Ac3DecoderTests.Data(name)), Ac3SpectralFrame.Create(attenuation, merged, blend));
        }

        [Theory]
        [InlineData(false, "spectral-rematrix.eac3")]
        [InlineData(true, "spectral-coupled.eac3")]
        public void ReproducesStereoCouplingAndRematrixVectors(bool coupled, string name)
        {
            Assert.Equal(File.ReadAllBytes(Ac3DecoderTests.Data(name)), Ac3SpectralFrame.Create(stereo: true, coupled: coupled));
        }

        [Theory]
        [InlineData("spectral.eac3")]
        [InlineData("spectral-noise.eac3")]
        public void ReusesReservedStorageAcrossFramesAndReset(string name)
        {
            byte[] data = File.ReadAllBytes(Ac3DecoderTests.Data(name));
            Ac3FrameHeader header = Ac3FrameHeader.Read(data);
            float[] pcm = new float[1536];
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (Ac3FrameDecoder decoder = new(allocator))
            {
                for (int iteration = 0; iteration < 8; iteration++) { decoder.Decode(data, header, pcm, CancellationToken.None); }
                long reserved = allocator.AllocatedBytes, before = GC.GetAllocatedBytesForCurrentThread();
                for (int iteration = 0; iteration < 128; iteration++) { decoder.Decode(data, header, pcm, CancellationToken.None); }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.Equal(0, allocated); Assert.Equal(33688, reserved); Assert.Equal(reserved, allocator.AllocatedBytes);
                decoder.Reset(); decoder.Decode(data, header, pcm, CancellationToken.None);
                Assert.Equal(reserved, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void NoiseBlendingPreservesTheBandedEnergyEnvelope()
        {
            Ac3TestBitWriter writer = new(64);
            Ac3SpectralFrame.WriteStrategy(writer, false);
            writer.Write(0, 5); writer.Write(0, 2);
            for (int band = 0; band < 3; band++) { writer.Write(0, 6); }
            Ac3BitReader bits = new(writer.Finish(true));
            MediaBufferAllocator allocator = new(2048);
            using (Ac3SpectralExtension extension = new(allocator))
            {
                extension.ResetFrame(); extension.ReadBlock(ref bits, 0, 1, true);
                double[] spectrum = new double[256];
                double energy = 0;
                uint random = 1;
                for (int iteration = 0; iteration < 512; iteration++)
                {
                    for (int bin = 25; bin < 49; bin++) { spectrum[bin] = (bin & 1) == 0 ? .01 : -.01; }
                    extension.Process(0, spectrum, ref random);
                    for (int bin = 49; bin < 85; bin++) { energy += spectrum[bin] * spectrum[bin]; }
                }
                double expected = 512 * 36 * .01 * .01 * 16 * 16;
                Assert.InRange(energy / expected, .98, 1.02);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void RejectsEmptyCopyRangesAndUndefinedReusedBands()
        {
            Ac3TestBitWriter range = new(16);
            range.Write(1, 1); range.Write(3, 2); range.Write(0, 6);
            MediaBufferAllocator allocator = new(2048);
            using (Ac3SpectralExtension extension = new(allocator))
            {
                extension.ResetFrame();
                InvalidDataException error = Assert.Throws<InvalidDataException>(() => ReadBlock(extension, range.Finish(true), 0));
                Assert.Contains("frequency range", error.Message, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(0, allocator.AllocatedBytes);
                Ac3TestBitWriter first = new(16);
                Ac3SpectralFrame.WriteStrategy(first, true); first.Write(31, 5); first.Write(0, 2); first.Write(0, 12);
                ReadBlock(extension, first.Finish(true), 0);
                Ac3TestBitWriter next = new(16);
                next.Write(3, 2); next.Write(0, 8); next.Write(1, 1); next.Write(0, 3);
                error = Assert.Throws<InvalidDataException>(() => ReadBlock(extension, next.Finish(true), 1));
                Assert.Contains("undefined bands", error.Message, StringComparison.OrdinalIgnoreCase);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static void ReadBlock(Ac3SpectralExtension extension, byte[] data, int block)
        {
            Ac3BitReader bits = new(data);
            extension.ReadBlock(ref bits, block, 1, true);
        }

        [Fact]
        public void SpectralStorageFailureDoesNotRetainBuffers()
        {
            byte[] data = Ac3SpectralFrame.Create();
            Ac3FrameHeader header = Ac3FrameHeader.Read(data);
            MediaBufferAllocator allocator = new(32328);
            using (Ac3FrameDecoder decoder = new(allocator))
            {
                Assert.Throws<MediaMemoryLimitException>(() => decoder.Decode(data, header, new float[1536], CancellationToken.None));
                Assert.Equal(32328, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}

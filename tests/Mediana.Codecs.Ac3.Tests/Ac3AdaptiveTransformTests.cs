// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Ac3.Tests
{
    public class Ac3AdaptiveTransformTests
    {
        [Theory]
        [InlineData(false, "adaptive.eac3")]
        [InlineData(true, "adaptive-switched.eac3")]
        public void ReproducesIndependentlyDecodedAdaptiveVectors(bool switched, string name)
        {
            Assert.Equal(File.ReadAllBytes(Ac3DecoderTests.Data(name)), Ac3AdaptiveFrame.Create(switched));
        }

        [Fact]
        public void ReproducesEveryVqCodebookAndGainAdaptiveModeInTheIndependentSequence()
        {
            Assert.Equal(File.ReadAllBytes(Ac3DecoderTests.Data("adaptive-quantizers.eac3")), Ac3AdaptiveQuantizerFrame.CreateSequence());
        }

        [Fact]
        public void NormativeFourthCodebookKeepsItsFirstAndLastEntries()
        {
            ReadOnlySpan<short> table = Ac3AhtVectorTables.Get(4);
            Assert.Equal(32 * 6, table.Length);
            Assert.Equal(new short[] { 22787, 5568, -5658, -156, -506, -33 }, table[..6].ToArray());
            Assert.Equal(new short[] { 7788, 1462, 18395, 15296, -15763, -1131 }, table[^6..].ToArray());
        }

        [Fact]
        public void ZeroAllocationNoiseHasTheExpectedSixBlockEnergy()
        {
            MediaBufferAllocator allocator = new(16384);
            using (Ac3AdaptiveTransform transform = new(allocator))
            {
                byte[] allocation = new byte[256], exponents = new byte[256], data = new byte[1];
                double[] spectrum = new double[256];
                uint random = 1;
                double energy = 0, sum = 0;
                for (int frame = 0; frame < 4096; frame++)
                {
                    transform.ResetFrame();
                    Ac3BitReader bits = new(data);
                    for (int block = 0; block < 6; block++)
                    {
                        transform.ReadSpectrum(ref bits, 0, block, 0, 1, allocation, exponents, spectrum, ref random);
                        double value = spectrum[0]; energy += value * value; sum += value;
                    }
                }
                Assert.InRange(energy / (4096 * 6), .48, .52);
                Assert.InRange(sum / (4096 * 6), -.02, .02);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void ReservedGainGroupsAndWorkspaceFailureReleaseStorage()
        {
            Ac3TestBitWriter writer = new(16);
            writer.Write(3, 2); writer.Write(31, 5);
            MediaBufferAllocator allocator = new(16384);
            using (Ac3AdaptiveTransform transform = new(allocator))
            {
                InvalidDataException error = Assert.Throws<InvalidDataException>(() => ReadInvalidGroup(transform, writer.Finish(true)));
                Assert.Contains("grouped adaptive gain", error.Message, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(12288, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            byte[] data = Ac3AdaptiveFrame.Create(false);
            Ac3FrameHeader header = Ac3FrameHeader.Read(data);
            allocator = new(32328);
            using (Ac3FrameDecoder decoder = new(allocator))
            {
                Assert.Throws<MediaMemoryLimitException>(() => decoder.Decode(data, header, new float[1536], CancellationToken.None));
                Assert.Equal(32328, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static void ReadInvalidGroup(Ac3AdaptiveTransform transform, byte[] data)
        {
            Ac3BitReader bits = new(data);
            byte[] allocation = new byte[256], exponents = new byte[256];
            allocation[0] = 8;
            double[] spectrum = new double[256];
            uint random = 1;
            transform.ReadSpectrum(ref bits, 0, 0, 0, 1, allocation, exponents, spectrum, ref random);
        }

        [Fact]
        public void ReusesReservedSixBlockStorageAndReleasesIt()
        {
            byte[] data = Ac3AdaptiveFrame.Create(true);
            Ac3FrameHeader header = Ac3FrameHeader.Read(data);
            float[] pcm = new float[1536];
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (Ac3FrameDecoder decoder = new(allocator))
            {
                for (int iteration = 0; iteration < 8; iteration++) { decoder.Decode(data, header, pcm, CancellationToken.None); }
                long reserved = allocator.AllocatedBytes, before = GC.GetAllocatedBytesForCurrentThread();
                for (int iteration = 0; iteration < 128; iteration++) { decoder.Decode(data, header, pcm, CancellationToken.None); }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.Equal(0, allocated); Assert.Equal(44616, reserved); Assert.Equal(reserved, allocator.AllocatedBytes);
                decoder.Reset(); decoder.Decode(data, header, pcm, CancellationToken.None);
                Assert.Equal(reserved, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}

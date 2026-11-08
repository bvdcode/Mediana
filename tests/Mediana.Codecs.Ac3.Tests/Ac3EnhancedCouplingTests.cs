// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Ac3.Tests
{
    public class Ac3EnhancedCouplingTests
    {
        [Theory]
        [InlineData(false, "enhanced-coupling.eac3")]
        [InlineData(true, "enhanced-coupling-merged.eac3")]
        public void ReproducesIndependentEnhancedCouplingSequences(bool merged, string name)
        {
            Assert.Equal(File.ReadAllBytes(Ac3DecoderTests.Data(name)), Ac3EnhancedCouplingFrame.CreateSequence(merged));
        }

        [Fact]
        public void ReproducesEnhancedCouplingWithSpectralExtension()
        {
            Assert.Equal(File.ReadAllBytes(Ac3DecoderTests.Data("enhanced-coupling-spectral.eac3")), Ac3EnhancedCouplingFrame.Create(spectral: true));
        }

        [Fact]
        public void EveryCompactAmplitudeMatchesTheNormativeMantissaAndExponent()
        {
            ReadOnlySpan<int> mantissas = [32, 27, 23, 19, 16, 27, 23, 19, 16, 27, 23, 19, 16, 27, 23, 19, 16,
                27, 23, 19, 16, 27, 23, 19, 16, 27, 23, 19, 16, 27, 23, 0];
            ReadOnlySpan<int> exponents = [0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4,
                5, 5, 5, 5, 6, 6, 6, 6, 7, 7, 0];
            for (int code = 0; code < 32; code++)
            { Assert.Equal(mantissas[code] / 32.0 / (1 << exponents[code]), Ac3EnhancedCoupling.DecodeAmplitude(code)); }
            Assert.Throws<ArgumentOutOfRangeException>(() => Ac3EnhancedCoupling.DecodeAmplitude(32));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        [InlineData(8)]
        [InlineData(9)]
        [InlineData(10)]
        [InlineData(11)]
        [InlineData(12)]
        [InlineData(13)]
        [InlineData(14)]
        [InlineData(15)]
        public void PublicDecoderPreservesTimingAndEveryStartFrequency(int begin)
        {
            byte[] data = Ac3EnhancedCouplingFrame.Create(begin);
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (IMediaCodec<MediaPacket, AudioFrame> decoder = Ac3DecoderTests.Create(true, allocator))
            using (MediaPacket packet = Ac3DecoderTests.Packet(data, allocator, 733))
            {
                Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet));
                using AudioFrame frame = decoder.Receive().Value;
                Assert.Equal(768, frame.SamplesPerChannel); Assert.Equal(packet.PresentationTime, frame.Timestamp);
                Assert.Equal(2, frame.Format.Channels.Count);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void ChangedBandStructureCannotReuseUndefinedAmplitudes()
        {
            MediaBufferAllocator allocator = new(1024);
            using (Ac3EnhancedCoupling coupling = new(allocator))
            {
                coupling.ResetFrame();
                Ac3TestBitWriter first = new(128);
                first.Write(6, 4); first.Write(15, 4); first.Write(0, 1); first.Write(0, 1);
                first.Write(0, 25); first.Write(0, 25); first.Write(0, 37);
                bool[] valid = new bool[5];
                Read(coupling, first.Finish(true), valid);
                Ac3TestBitWriter changed = new(128);
                changed.Write(6, 4); changed.Write(15, 4); changed.Write(1, 1); changed.Write(0, 13);
                changed.Write(0, 2);
                InvalidDataException error = Assert.Throws<InvalidDataException>(() => Read(coupling, changed.Finish(true), valid));
                Assert.Contains("undefined bands", error.Message, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(880, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static void Read(Ac3EnhancedCoupling coupling, byte[] data, bool[] valid)
        {
            Ac3BitReader bits = new(data);
            coupling.ReadStrategy(ref bits, false, 0);
            coupling.ReadCoordinates(ref bits, new[] { true, true }, valid, 2);
        }

        [Fact]
        public void NonAdjacentCoupledChannelsKeepTheirOwnAmplitudeAndReuseFlags()
        {
            MediaBufferAllocator allocator = new(1024);
            using (Ac3EnhancedCoupling coupling = new(allocator))
            {
                coupling.ResetFrame();
                Ac3TestBitWriter writer = new(128);
                writer.Write(15, 4); writer.Write(15, 4); writer.Write(0, 1); writer.Write(1, 1);
                writer.Write(0, 5); writer.Write(31, 5); writer.Write(1, 1);
                Ac3BitReader bits = new(writer.Finish(true));
                coupling.ReadStrategy(ref bits, false, 0);
                bool[] channels = [false, true, false, false, true], valid = new bool[5];
                coupling.ReadCoordinates(ref bits, channels, valid, 5);
                Assert.Equal(21, bits.Position);
                Assert.Equal(channels, valid);
                double[] common = Enumerable.Repeat(1.0, 256).ToArray(), first = new double[256], last = new double[256];
                byte[] allocation = Enumerable.Repeat((byte)15, 256).ToArray(), exponents = new byte[256];
                uint random = 1;
                coupling.Process(1, first, common, allocation, exponents, false, ref random);
                coupling.Process(4, last, common, allocation, exponents, false, ref random);
                Assert.All(first[229..253], value => Assert.Equal(1, value));
                Assert.All(last, value => Assert.Equal(0, value));
                Ac3BitReader reused = new(new byte[1]);
                coupling.ReadCoordinates(ref reused, channels, valid, 5);
                Assert.Equal(5, reused.Position);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void InvalidFrequencyRangeAndQuotaFailureDoNotLeakStorage()
        {
            MediaBufferAllocator allocator = new(1024);
            using (Ac3EnhancedCoupling coupling = new(allocator))
            {
                Ac3TestBitWriter writer = new(16); writer.Write(15, 4); writer.Write(0, 4);
                Assert.Throws<InvalidDataException>(() => Read(coupling, writer.Finish(true), new bool[5]));
                Assert.Equal(0, allocator.AllocatedBytes);
            }
            byte[] data = Ac3EnhancedCouplingFrame.Create();
            allocator = new(32328);
            using (Ac3FrameDecoder decoder = new(allocator))
            {
                Assert.Throws<MediaMemoryLimitException>(() => decoder.Decode(data, Ac3FrameHeader.Read(data), new float[1536], CancellationToken.None));
                Assert.Equal(32328, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void RepeatedDecodingAndResetReuseTheReservedAmplitudeStorage()
        {
            byte[] data = Ac3EnhancedCouplingFrame.Create();
            Ac3FrameHeader header = Ac3FrameHeader.Read(data);
            float[] pcm = new float[1536];
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (Ac3FrameDecoder decoder = new(allocator))
            {
                for (int iteration = 0; iteration < 16; iteration++) { decoder.Decode(data, header, pcm, CancellationToken.None); }
                long reserved = allocator.AllocatedBytes, before = GC.GetAllocatedBytesForCurrentThread();
                for (int iteration = 0; iteration < 128; iteration++) { decoder.Decode(data, header, pcm, CancellationToken.None); }
                Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
                Assert.Equal(33208, reserved); Assert.Equal(reserved, allocator.AllocatedBytes);
                decoder.Reset(); decoder.Decode(data, header, pcm, CancellationToken.None);
                Assert.Equal(reserved, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}

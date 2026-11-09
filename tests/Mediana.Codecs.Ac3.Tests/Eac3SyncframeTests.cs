// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Formats;
using Mediana.Tests;

namespace Mediana.Codecs.Ac3.Tests
{
    public class Eac3SyncframeTests
    {
        [Theory]
        [InlineData("stereo.eac3", "stereo.pcm", 2, 60)]
        [InlineData("dependent-8.eac3", "dependent-8.pcm", 8, 135)]
        [InlineData("dependent-16.eac3", "dependent-16.pcm", 16, 135)]
        [InlineData("transient.eac3", "transient.pcm", 1, 135)]
        [InlineData("transient-tail.eac3", "transient-tail.pcm", 1, 135)]
        [InlineData("adaptive-quantizers.eac3", "adaptive-quantizers.pcm", 1, 135)]
        [InlineData("enhanced-coupling.eac3", "enhanced-coupling.pcm", 2, 135)]
        public Task IndividualSyncframesPreserveTheCompleteProgrammeWithoutCopyingEncodedData(string encoded, string reference, int channels, double minimumSnr) =>
            Ac3PcmTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data"), encoded, reference, 48000, channels, minimumSnr, true);

        [Fact]
        public void IncompleteFinalCompositionIsRejectedAndEveryRetainedLeaseIsReleased()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (IMediaCodec<MediaPacket, AudioFrame> decoder = Create(allocator))
            {
                foreach (int component in new[] { 0, 1, 0 })
                {
                    using MediaPacket packet = Ac3DecoderTests.Packet(Ac3DependentFrame.Create(component), allocator);
                    Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet));
                }
                Assert.Throws<InvalidDataException>(() => decoder.CompleteInput());
                Assert.Equal(0, allocator.AllocatedBytes);
                Assert.Throws<InvalidOperationException>(() => decoder.Receive());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void ResetAndCancellationReleaseTheBorrowedFrameSetBeforeSynthesis()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (IMediaCodec<MediaPacket, AudioFrame> decoder = Create(allocator))
            {
                byte[] first = Ac3DependentFrame.Create(0);
                using (MediaPacket packet = Ac3DecoderTests.Packet(first, allocator)) { decoder.TrySend(packet); }
                Assert.Equal(first.Length, allocator.AllocatedBytes);
                decoder.Reset(); Assert.Equal(0, allocator.AllocatedBytes);
                using (MediaPacket packet = Ac3DecoderTests.Packet(first, allocator)) { decoder.TrySend(packet); }
                using (MediaPacket packet = Ac3DecoderTests.Packet(Ac3DependentFrame.Create(1), allocator))
                { Assert.ThrowsAny<OperationCanceledException>(() => decoder.TrySend(packet, new CancellationToken(true))); }
                Assert.Equal(0, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void DependentTimestampsMustIdentifyTheSameProgrammeInterval()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (IMediaCodec<MediaPacket, AudioFrame> decoder = Create(allocator))
            {
                using (MediaPacket packet = Ac3DecoderTests.Packet(Ac3DependentFrame.Create(0), allocator)) { decoder.TrySend(packet); }
                using (MediaPacket packet = Ac3DecoderTests.Packet(Ac3DependentFrame.Create(1), allocator, 768))
                { Assert.Throws<InvalidDataException>(() => decoder.TrySend(packet)); }
                Assert.Equal(0, allocator.AllocatedBytes);
            }
        }

        private static IMediaCodec<MediaPacket, AudioFrame> Create(MediaBufferAllocator allocator) =>
            new Eac3DecoderFactory(false).Create(new(0, MediaKind.Audio, "eac3", StreamFormats.Eac3Syncframe, new(1, 48000)), allocator);
    }
}

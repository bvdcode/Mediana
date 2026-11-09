// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Codecs.Ac3.Tests
{
    public class Ac3TransientProcessingTests
    {
        [Fact]
        public void ReproducesIndependentTimeScalingAndOutOfOrderTransientSequence()
        {
            Assert.Equal(File.ReadAllBytes(Ac3DecoderTests.Data("transient.eac3")), Ac3TransientFrame.CreateSequence());
            Assert.Equal(File.ReadAllBytes(Ac3DecoderTests.Data("transient-tail.eac3")), Ac3TransientFrame.CreateTailSequence());
        }

        [Fact]
        public void MissingHistoryFailsBeforePublishingApproximatePcm()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (IMediaCodec<MediaPacket, AudioFrame> decoder = Ac3DecoderTests.Create(true, allocator))
            {
                using (MediaPacket invalid = Ac3DecoderTests.Packet(Ac3SyntheticFrame.Create(true, false, 1, transientLocation: 0), allocator))
                { Assert.Throws<InvalidDataException>(() => decoder.TrySend(invalid)); }
                Assert.Throws<InvalidOperationException>(() => decoder.Receive());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void FutureCorrectionOutsideTheDecodedExtentDoesNotChangeItsPcm()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            byte[] expected = Decode(null);
            Assert.Equal(expected, Decode(4092));
            Assert.Equal(0, allocator.AllocatedBytes);

            byte[] Decode(int? location)
            {
                using IMediaCodec<MediaPacket, AudioFrame> decoder = Ac3DecoderTests.Create(true, allocator);
                using MediaPacket packet = Ac3DecoderTests.Packet(Ac3SyntheticFrame.Create(true, false, 1, transientLocation: location), allocator);
                decoder.TrySend(packet); Assert.Equal(CodecOutputStatus.InputRequired, decoder.Receive().Status);
                decoder.CompleteInput();
                using AudioFrame frame = decoder.Receive().Value;
                Assert.Equal(256, frame.SamplesPerChannel);
                Assert.Equal(CodecOutputStatus.EndOfStream, decoder.Receive().Status);
                return frame.GetPlane(0).ToArray();
            }
        }

        [Fact]
        public void NonOriginRecoveryIsConfirmedAfterActualWarmup()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            byte[] data = Ac3SyntheticFrame.Create(true, false, 1);
            using (IMediaCodec<MediaPacket, AudioFrame> decoder = Ac3DecoderTests.Create(true, allocator))
            {
                ((IPacketPrerollDecoder)decoder).ResetForPreroll(26, false);
                int outputs = 0;
                for (int frame = 0; frame < 52; frame++)
                {
                    using MediaPacket packet = Ac3DecoderTests.Packet(data, allocator, 100000 + frame * 256);
                    Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet));
                    while (decoder.Receive() is { Status: CodecOutputStatus.Available } available)
                    {
                        using AudioFrame output = available.Value;
                        Assert.Equal(new MediaTimestamp(100000 + (26 + outputs) * 256, new(1, 48000)), output.Timestamp); outputs++;
                    }
                }
                decoder.CompleteInput();
                while (decoder.Receive() is { Status: CodecOutputStatus.Available } available)
                { using AudioFrame output = available.Value; outputs++; }
                Assert.Equal(26, outputs);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void TransientChainsPropagateUncertainHistoryBeyondAnInitialPacketCount()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            byte[] data = Ac3SyntheticFrame.Create(true, false, 1, transientLocation: 1536, transientLength: 255);
            using (IMediaCodec<MediaPacket, AudioFrame> decoder = Ac3DecoderTests.Create(true, allocator))
            {
                ((IPacketPrerollDecoder)decoder).ResetForPreroll(26, false);
                NotSupportedException error = Assert.Throws<NotSupportedException>(() =>
                {
                    for (int frame = 0; frame < 52; frame++)
                    {
                        using MediaPacket packet = Ac3DecoderTests.Packet(data, allocator, frame * 256);
                        Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet));
                        CodecOutput<AudioFrame> output = decoder.Receive();
                        Assert.Equal(CodecOutputStatus.InputRequired, output.Status);
                    }
                });
                Assert.Contains("recovery", error.Message, StringComparison.OrdinalIgnoreCase);
                Assert.Throws<InvalidOperationException>(() => decoder.Receive());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}

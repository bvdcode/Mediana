// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Codecs.Ac3;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Formats;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Ac3.Tests
{
    public class Ac3DecoderTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PreservesClockOwnershipBackpressureAndClippedPresentation(bool enhanced)
        {
            byte[] encoded = ReadFirstFrame(enhanced ? "stereo.eac3" : "mono.ac3");
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (IMediaCodec<MediaPacket, AudioFrame> decoder = Create(enhanced, allocator))
            using (MediaPacket packet = Packet(encoded, allocator, 733, 1000))
            {
                Assert.Equal(CodecOutputStatus.InputRequired, decoder.Receive().Status);
                Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet));
                if (enhanced)
                {
                    for (int index = 1; index < 5; index++)
                    { using MediaPacket following = Packet(encoded, allocator, 733 + 1536 * index); Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(following)); }
                }
                Assert.Equal(CodecInputStatus.OutputRequired, decoder.TrySend(packet));
                using AudioFrame frame = decoder.Receive().Value;
                Assert.Equal(new MediaTimestamp(733, new(1, 48000)), frame.Timestamp);
                Assert.Equal(1000, frame.SamplesPerChannel); Assert.Equal(new MediaDuration(1000, new(1, 48000)), frame.Duration);
                Assert.Equal(enhanced ? 2 : 1, frame.Format.Channels.Count);
                Assert.Equal(1000 * frame.Format.Channels.Count * sizeof(float), frame.GetPlane(0).Length);
                byte[] retained = frame.GetPlane(0).ToArray();
                decoder.Reset();
                Assert.Equal(retained, frame.GetPlane(0).ToArray());
                decoder.CompleteInput(); Assert.Equal(CodecOutputStatus.EndOfStream, decoder.Receive().Status);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OriginPrerollReconstructsTheSameFollowingPcm(bool enhanced)
        {
            byte[] encoded = File.ReadAllBytes(Data(enhanced ? "stereo.eac3" : "mono.ac3"));
            Ac3FrameHeader first = Ac3FrameHeader.Read(encoded);
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (IMediaCodec<MediaPacket, AudioFrame> decoder = Create(enhanced, allocator))
            using (MediaPacket firstPacket = Packet(encoded.AsSpan(0, first.FrameBytes), allocator))
            using (MediaPacket secondPacket = Packet(encoded.AsSpan(first.FrameBytes, Ac3FrameHeader.Read(encoded.AsSpan(first.FrameBytes)).FrameBytes), allocator, 1536))
            {
                decoder.TrySend(firstPacket);
                if (!enhanced) { decoder.Receive().Value.Dispose(); }
                decoder.TrySend(secondPacket); decoder.CompleteInput();
                if (enhanced) { decoder.Receive().Value.Dispose(); }
                using AudioFrame expected = decoder.Receive().Value;
                IPacketPrerollDecoder seeking = Assert.IsAssignableFrom<IPacketPrerollDecoder>(decoder);
                Assert.Equal(enhanced ? 26 : 1, seeking.RequiredPrerollPackets);
                seeking.ResetForPreroll(1, true);
                decoder.TrySend(firstPacket); Assert.Equal(CodecOutputStatus.InputRequired, decoder.Receive().Status);
                decoder.TrySend(secondPacket); decoder.CompleteInput();
                using AudioFrame actual = decoder.Receive().Value;
                Assert.Equal(expected.GetPlane(0).ToArray(), actual.GetPlane(0).ToArray());
                Assert.Equal(secondPacket.PresentationTime, actual.Timestamp);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CancellationFaultsTheDecoderAndResetRestoresIt(bool enhanced)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (IMediaCodec<MediaPacket, AudioFrame> decoder = Create(enhanced, allocator))
            using (MediaPacket packet = Packet(ReadFirstFrame(enhanced ? "stereo.eac3" : "mono.ac3"), allocator))
            {
                using CancellationTokenSource cancellation = new(); cancellation.Cancel();
                Assert.Throws<OperationCanceledException>(() => decoder.TrySend(packet, cancellation.Token));
                Assert.Throws<InvalidOperationException>(() => decoder.Receive());
                decoder.Reset(); decoder.TrySend(packet);
                Assert.Throws<OperationCanceledException>(() => decoder.Receive(cancellation.Token));
                Assert.Throws<InvalidOperationException>(() => decoder.TrySend(packet));
                decoder.Reset(); decoder.TrySend(packet); decoder.CompleteInput();
                using AudioFrame frame = decoder.Receive().Value;
                Assert.Equal(1536, frame.SamplesPerChannel); Assert.Equal(CodecOutputStatus.EndOfStream, decoder.Receive().Status);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RejectsCorruptionAndTruncationWithoutLeakingStorage(bool enhanced)
        {
            byte[] bytes = ReadFirstFrame(enhanced ? "stereo.eac3" : "mono.ac3");
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (IMediaCodec<MediaPacket, AudioFrame> decoder = Create(enhanced, allocator))
            {
                bytes[bytes.Length / 2] ^= 1;
                using (MediaPacket packet = Packet(bytes, allocator)) { Assert.Throws<InvalidDataException>(() => decoder.TrySend(packet)); }
                Assert.Equal(0, allocator.AllocatedBytes);
                decoder.Reset();
                using (MediaPacket packet = Packet(bytes.AsSpan(0, bytes.Length - 1), allocator)) { Assert.Throws<InvalidDataException>(() => decoder.TrySend(packet)); }
                Assert.Equal(0, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void FailureToReserveWorkspaceReleasesItsReservation()
        {
            MediaBufferAllocator allocator = new(1024);
            MediaBufferAllocator inputAllocator = new(1024 * 1024);
            using (IMediaCodec<MediaPacket, AudioFrame> decoder = Create(false, allocator))
            using (MediaPacket packet = Packet(ReadFirstFrame("mono.ac3"), inputAllocator))
            {
                Assert.Throws<MediaMemoryLimitException>(() => decoder.TrySend(packet));
                Assert.Equal(0, allocator.AllocatedBytes);
            }
            Assert.Equal(0, inputAllocator.AllocatedBytes);
        }

        [Fact]
        public void RejectsWrongCodecStreamAndFraming()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            Ac3DecoderFactory factory = new();
            EncodedStreamInfo stream = new(0, MediaKind.Audio, "ac3", StreamFormats.Eac3, new(1, 48000));
            Assert.NotNull(factory.GetUnsupportedReason(stream)); Assert.Throws<NotSupportedException>(() => factory.Create(stream, allocator));
            using (IMediaCodec<MediaPacket, AudioFrame> decoder = Create(false, allocator))
            using (MediaPacket packet = Packet(ReadFirstFrame("stereo.eac3"), allocator)) { Assert.Throws<InvalidDataException>(() => decoder.TrySend(packet)); }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void RejectsFractionalSamplePresentationAndIncompletePreroll()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (IMediaCodec<MediaPacket, AudioFrame> decoder = Create(false, allocator))
            using (WritableMediaBuffer writable = allocator.Allocate(ReadFirstFrame("mono.ac3").Length))
            {
                ReadFirstFrame("mono.ac3").CopyTo(writable.Span);
                using MediaBuffer buffer = writable.Publish();
                using MediaPacket packet = new(0, buffer, duration: new(1, new(1, 48001)));
                Assert.Throws<InvalidDataException>(() => decoder.TrySend(packet));
                ((IPacketPrerollDecoder)decoder).ResetForPreroll(1, false);
                Assert.Throws<InvalidDataException>(() => decoder.CompleteInput());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(1536)]
        [InlineData(1000)]
        [InlineData(0)]
        public void Eac3AccessUnitCombinesSyncframesWithoutChangingThePresentationClock(int presentationSamples)
        {
            byte[][] parts = [Ac3SyntheticFrame.Create(true, false, 1), Ac3SyntheticFrame.Create(true, false, 2), Ac3SyntheticFrame.Create(true, false, 3)];
            byte[] bundle = [.. parts[0], .. parts[1], .. parts[2]];
            float[] expected = new float[1536];
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (IMediaCodec<MediaPacket, AudioFrame> decoder = Create(true, allocator))
            {
                int offset = 0;
                foreach (byte[] part in parts)
                {
                    using MediaPacket packet = Packet(part, allocator, offset);
                    decoder.TrySend(packet); Assert.Equal(CodecOutputStatus.InputRequired, decoder.Receive().Status);
                    offset += Ac3FrameHeader.Read(part).SamplesPerFrame;
                }
                decoder.CompleteInput(); offset = 0;
                while (decoder.Receive() is { Status: CodecOutputStatus.Available } available)
                {
                    using AudioFrame output = available.Value;
                    MemoryMarshal.Cast<byte, float>(output.GetPlane(0).Span).CopyTo(expected.AsSpan(offset)); offset += output.SamplesPerChannel;
                }
                Assert.Equal(1536, offset);
                decoder.Reset();
                using MediaPacket combined = Packet(bundle, allocator, 733, presentationSamples);
                Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(combined)); decoder.CompleteInput();
                if (presentationSamples == 0) { Assert.Equal(CodecOutputStatus.EndOfStream, decoder.Receive().Status); }
                else
                {
                    using AudioFrame actual = decoder.Receive().Value;
                    Assert.Equal(presentationSamples, actual.SamplesPerChannel); Assert.Equal(combined.PresentationTime, actual.Timestamp);
                    Assert.Equal(expected.AsSpan(0, presentationSamples).ToArray(), MemoryMarshal.Cast<byte, float>(actual.GetPlane(0).Span).ToArray());
                    Assert.Equal(CodecOutputStatus.EndOfStream, decoder.Receive().Status);
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void RejectsExcessBlocksOrCorruptedLaterSyncframesBeforePublishingPcm()
        {
            byte[] part = Ac3SyntheticFrame.Create(true, false, 3);
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (IMediaCodec<MediaPacket, AudioFrame> decoder = Create(true, allocator))
            {
                using (MediaPacket excessive = Packet([.. part, .. part, .. part], allocator)) { Assert.Throws<InvalidDataException>(() => decoder.TrySend(excessive)); }
                decoder.Reset();
                byte[] bundle = [.. part, .. part]; bundle[^5] ^= 1;
                using MediaPacket corrupt = Packet(bundle, allocator);
                Assert.Throws<InvalidDataException>(() => decoder.TrySend(corrupt));
                Assert.Throws<InvalidOperationException>(() => decoder.Receive());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        internal static IMediaCodec<MediaPacket, AudioFrame> Create(bool enhanced, MediaBufferAllocator allocator)
        {
            Ac3DecoderFactory factory = enhanced ? new Eac3DecoderFactory() : new Ac3DecoderFactory();
            EncodedStreamInfo stream = new(0, MediaKind.Audio, factory.CodecId, enhanced ? StreamFormats.Eac3 : StreamFormats.Ac3, new(1, 48000));
            return factory.Create(stream, allocator);
        }
        internal static MediaPacket Packet(ReadOnlySpan<byte> bytes, MediaBufferAllocator allocator, long timestamp = 0, int? count = null)
        {
            using WritableMediaBuffer writable = allocator.Allocate(bytes.Length); bytes.CopyTo(writable.Span);
            using MediaBuffer buffer = writable.Publish();
            return new(0, buffer, new(timestamp, new(1, 48000)), duration: count is not null ? new(count.Value, new(1, 48000)) : null);
        }
        internal static byte[] ReadFirstFrame(string name)
        {
            byte[] bytes = File.ReadAllBytes(Data(name)); return bytes[..Ac3FrameHeader.Read(bytes).FrameBytes];
        }
        internal static string Data(string name) => Path.Combine(AppContext.BaseDirectory, "Data", name);
    }
}

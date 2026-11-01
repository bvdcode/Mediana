// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Pcm;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.IO;

namespace Mediana.Tests
{
    public class ContainerDecodeTransferTests
    {
        [Fact]
        public async Task SuccessfulVideoTransferKeepsReaderMetadataAndReturnedStorage()
        {
            FixturePictureContainerFactory factory = new();
            FixtureDecoderFactory decoder = new();
            MediaEngine engine = new MediaEngineBuilder().AddContainer(factory).AddVideoDecoder(decoder).Build();
            using MemoryStream stream = new([.. "TEST"u8, 11, 22]);
            await using RandomAccessStreamByteSource source = new(stream, leaveOpen: true);
            await source.ReadAsync(new byte[2]);
            await using ContainerSession container = await engine.OpenContainerAsync(source);
            EncodedStreamInfo selected = container.Streams[0];
            using MediaBuffer picture = await container.ReadPictureAsync(0);
            using MediaBuffer attachment = await container.ReadAttachmentAsync(0);
            await using VideoSession video = engine.OpenVideo(container);
            Assert.Same(selected, video.Stream);
            await container.DisposeAsync();
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await container.ReadPacketAsync());
            Assert.Throws<ObjectDisposedException>(() => engine.OpenVideo(container));
            using VideoFrame retained = Assert.IsType<VideoFrame>(await video.ReadFrameAsync());
            Assert.Equal(11, retained.Data.Span[0]);
            using (VideoFrame next = Assert.IsType<VideoFrame>(await video.ReadFrameAsync()))
            {
                Assert.Equal(22, next.Data.Span[0]);
            }
            Assert.Null(await video.ReadFrameAsync());
            await video.DisposeAsync();
            Assert.True(decoder.LastDecoder!.IsDisposed);
            Assert.Equal(11, retained.Data.Span[0]);
            Assert.Equal(new byte[] { 7, 8, 9 }, picture.Memory.ToArray());
            Assert.Equal(2, stream.Position);
            Assert.True(stream.CanRead);
            picture.Dispose();
            attachment.Dispose();
            retained.Dispose();
            Assert.Equal(0, container.AllocatedBufferBytes);
        }

        [Fact]
        public async Task AudioTransferUsesOneDemuxAndPreservesExplicitSelection()
        {
            InterleavedContainerFactory factory = new();
            MediaEngine engine = new MediaEngineBuilder().AddContainer(factory).AddAudioDecoder(new PcmDecoderFactory()).Build();
            await using ShortReadSource source = new([10, 11, 12, 13]);
            await using ContainerSession container = await engine.OpenContainerAsync(source);
            await using AudioSession audio = engine.OpenAudio(container, 7);
            List<byte> samples = [];
            while (await audio.ReadFrameAsync() is Core.Audio.AudioFrame frame)
            {
                using (frame) { samples.Add(frame.GetPlane(0).Span[0]); }
            }
            Assert.Equal(new byte[] { 11, 13 }, samples);
            Assert.Equal(1, factory.Opens);
            Assert.Equal(5, factory.Reader!.Reads);
            await audio.DisposeAsync();
            Assert.True(factory.Reader.IsDisposed);
            Assert.False(source.IsDisposed);
            Assert.Equal(0, container.AllocatedBufferBytes);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task OriginalPacketAndFrameLimitsApplyAfterTransfer(bool packetLimit)
        {
            FixtureDecoderFactory decoder = new();
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new FixtureContainerFactory()).AddVideoDecoder(decoder).Build();
            await using ShortReadSource source = new([.. "TEST"u8, 11, 22, 33]);
            MediaOpenOptions options = packetLimit ? new() { MaxPacketReads = 1 } : new() { MaxFrameReads = 1 };
            await using ContainerSession container = await engine.OpenContainerAsync(source, options);
            await using VideoSession video = engine.OpenVideo(container);
            if (!packetLimit)
            {
                using VideoFrame? frame = await video.ReadFrameAsync();
                Assert.NotNull(frame);
            }
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await video.ReadFrameAsync());
            Assert.True(decoder.LastDecoder!.IsDisposed);
            await video.DisposeAsync();
            Assert.Equal(0, container.AllocatedBufferBytes);
            Assert.False(source.IsDisposed);
        }

        [Fact]
        public async Task InvalidSelectionOrCancellationLeavesTheContainerAvailableForAnotherDecoder()
        {
            FixtureContainerFactory factory = new();
            MediaEngine engine = new MediaEngineBuilder().AddContainer(factory).AddVideoDecoder(new FixtureDecoderFactory()).Build();
            await using ShortReadSource source = new([.. "TEST"u8, 11]);
            await using ContainerSession container = await engine.OpenContainerAsync(source);
            Assert.Throws<ArgumentOutOfRangeException>(() => engine.OpenVideo(container, -1));
            Assert.Throws<ArgumentException>(() => engine.OpenVideo(container, 9));
            Assert.Throws<NotSupportedException>(() => engine.OpenAudio(container, 0));
            Assert.ThrowsAny<OperationCanceledException>(() => engine.OpenVideo(container, cancellationToken: new(true)));
            MediaEngine missing = new MediaEngineBuilder().Build();
            Assert.Throws<NotSupportedException>(() => missing.OpenVideo(container));
            MediaEngine ambiguous = new MediaEngineBuilder().AddVideoDecoder(new FixtureDecoderFactory("first"))
                .AddVideoDecoder(new FixtureDecoderFactory("second")).Build();
            Assert.Throws<NotSupportedException>(() => ambiguous.OpenVideo(container));
            Assert.False(factory.LastReader!.IsDisposed);
            Assert.Equal(0, factory.LastReader.PacketReads);
            await using VideoSession video = engine.OpenVideo(container);
            using VideoFrame? frame = await video.ReadFrameAsync();
            Assert.NotNull(frame);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task FailureAfterDecoderCreationReleasesStorageWithoutClosingTheContainer(bool cancellation)
        {
            using CancellationTokenSource token = new();
            TransferInspectionTestFactory decoder = new(cancellation ? token.Cancel : () => throw new InvalidDataException("Invalid inspector."));
            FixtureContainerFactory factory = new();
            MediaEngine engine = new MediaEngineBuilder().AddContainer(factory).AddVideoDecoder(decoder).Build();
            await using ShortReadSource source = new([.. "TEST"u8, 11]);
            await using ContainerSession container = await engine.OpenContainerAsync(source);
            long before = container.AllocatedBufferBytes;
            if (cancellation)
            {
                Assert.ThrowsAny<OperationCanceledException>(() => engine.OpenVideo(container, cancellationToken: token.Token));
            }
            else
            {
                Assert.Throws<InvalidDataException>(() => engine.OpenVideo(container));
            }
            Assert.True(decoder.Inner.LastDecoder!.IsDisposed);
            Assert.Equal(before, container.AllocatedBufferBytes);
            Assert.False(factory.LastReader!.IsDisposed);
            using MediaPacket packet = Assert.IsType<MediaPacket>(await container.ReadPacketAsync());
            Assert.Equal(11, packet.Data.Span[0]);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task AnyPacketReadIncludingUnlimitedEofPreventsTransfer(bool eof)
        {
            FixtureDecoderFactory decoder = new();
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new FixtureContainerFactory()).AddVideoDecoder(decoder).Build();
            await using ShortReadSource source = new(eof ? [.. "TEST"u8] : [.. "TEST"u8, 11]);
            await using ContainerSession container = await engine.OpenContainerAsync(source);
            using MediaPacket? packet = await container.ReadPacketAsync();
            Assert.Equal(eof, packet is null);
            Assert.Throws<InvalidOperationException>(() => engine.OpenVideo(container));
            Assert.Null(decoder.LastDecoder);
            await container.DisposeAsync();
            Assert.Throws<ObjectDisposedException>(() => engine.OpenVideo(container));
        }

        [Fact]
        public async Task InFlightAndFaultedReaderOperationsCannotBeTransferred()
        {
            FixturePictureContainerFactory factory = new();
            FixtureDecoderFactory decoder = new();
            MediaEngine engine = new MediaEngineBuilder().AddContainer(factory).AddVideoDecoder(decoder).Build();
            await using ShortReadSource source = new([.. "TEST"u8, 11]);
            await using ContainerSession container = await engine.OpenContainerAsync(source);
            TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            factory.LastReader!.BeforePictureRead = async token =>
            {
                entered.SetResult();
                await release.Task.WaitAsync(token);
            };
            using CancellationTokenSource cancellation = new();
            Task<MediaBuffer> pending = container.ReadPictureAsync(0, cancellation.Token).AsTask();
            await entered.Task;
            Assert.Throws<InvalidOperationException>(() => engine.OpenVideo(container));
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            Assert.Throws<InvalidOperationException>(() => engine.OpenVideo(container));
            Assert.Null(decoder.LastDecoder);
        }
    }
}

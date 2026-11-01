// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class ContainerAttachmentSessionTests
    {
        [Fact]
        public async Task UnsupportedAttachmentsDoNotPreventPacketReading()
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new FixtureContainerFactory()).Build();
            await using ShortReadSource source = new([.. "TEST"u8, 11]);
            await using ContainerSession session = await engine.OpenContainerAsync(source);
            Assert.Null(session.Attachments);
            await Assert.ThrowsAsync<NotSupportedException>(async () => await session.ReadAttachmentAsync(0));
            using MediaPacket packet = Assert.IsType<MediaPacket>(await session.ReadPacketAsync());
            Assert.Equal(11, packet.Data.Span[0]);
        }

        [Fact]
        public async Task AttachmentReadsPreservePacketBudgetAndStorageSurvivesDisposal()
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new FixturePictureContainerFactory()).Build();
            await using ShortReadSource source = new([.. "TEST"u8, 11]);
            ContainerSession session = await engine.OpenContainerAsync(source, new MediaOpenOptions { MaxPacketReads = 1 });
            ContainerAttachmentInfo info = Assert.Single(session.Attachments!);
            Assert.Equal(3, info.ByteLength);
            Assert.Equal("cover.png", info.FileName);
            Assert.Equal("image/png", info.MediaType);
            Assert.Equal("Front", info.Description);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await session.ReadAttachmentAsync(-1));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await session.ReadAttachmentAsync(1));
            using (MediaBuffer data = await session.ReadAttachmentAsync(0))
            {
                using MediaPacket packet = Assert.IsType<MediaPacket>(await session.ReadPacketAsync());
                Assert.Equal(11, packet.Data.Span[0]);
                await session.DisposeAsync();
                Assert.Equal(new byte[] { 7, 8, 9 }, data.Memory.ToArray());
            }
            Assert.Equal(0, session.AllocatedBufferBytes);
            Assert.False(source.IsDisposed);
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await session.ReadAttachmentAsync(0));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task AttachmentFailureOrCancellationFaultsSessionAndExcludesOtherOperations(bool cancel)
        {
            FixturePictureContainerFactory factory = new();
            MediaEngine engine = new MediaEngineBuilder().AddContainer(factory).Build();
            await using ShortReadSource source = new([.. "TEST"u8, 11]);
            await using ContainerSession session = await engine.OpenContainerAsync(source);
            using CancellationTokenSource cancellation = new();
            TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource proceed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            factory.LastReader!.BeforeAttachmentRead = async token =>
            {
                entered.SetResult();
                await proceed.Task.WaitAsync(token);
                throw new InvalidDataException("The attachment is truncated.");
            };
            Task<MediaBuffer> pending = session.ReadAttachmentAsync(0, cancellation.Token).AsTask();
            await entered.Task;
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.ReadPictureAsync(0));
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.ReadAttachmentAsync(0));
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.ReadPacketAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.DisposeAsync());
            if (cancel)
            {
                await cancellation.CancelAsync();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            }
            else
            {
                proceed.SetResult();
                await Assert.ThrowsAsync<InvalidDataException>(() => pending);
            }
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.ReadAttachmentAsync(0));
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.ReadPictureAsync(0));
            await session.DisposeAsync();
            Assert.Equal(0, session.AllocatedBufferBytes);
            Assert.False(source.IsDisposed);
        }
    }
}

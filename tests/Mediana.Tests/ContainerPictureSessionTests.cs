// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class ContainerPictureSessionTests
    {
        [Fact]
        public async Task UnsupportedPicturesAreDistinctAndDoNotPreventPacketReading()
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new FixtureContainerFactory()).Build();
            await using ShortReadSource source = new([.. "TEST"u8, 11]);
            await using ContainerSession session = await engine.OpenContainerAsync(source);
            Assert.Null(session.Pictures);
            await Assert.ThrowsAsync<NotSupportedException>(async () => await session.ReadPictureAsync(0));
            using MediaPacket packet = Assert.IsType<MediaPacket>(await session.ReadPacketAsync());
            Assert.Equal(11, packet.Data.Span[0]);
        }

        [Fact]
        public async Task PictureReadsPreservePacketBudgetAndStorageSurvivesSessionDisposal()
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new FixturePictureContainerFactory()).Build();
            await using ShortReadSource source = new([.. "TEST"u8, 11]);
            ContainerSession session = await engine.OpenContainerAsync(source, new MediaOpenOptions { MaxPacketReads = 1 });
            ContainerPictureInfo info = Assert.Single(session.Pictures!);
            Assert.Equal(3, info.ByteLength);
            Assert.Equal(PictureDataKind.Embedded, info.DataKind);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await session.ReadPictureAsync(-1));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await session.ReadPictureAsync(1));
            using (MediaBuffer picture = await session.ReadPictureAsync(0))
            {
                using MediaPacket packet = Assert.IsType<MediaPacket>(await session.ReadPacketAsync());
                Assert.Equal(11, packet.Data.Span[0]);
                await session.DisposeAsync();
                Assert.Equal(new byte[] { 7, 8, 9 }, picture.Memory.ToArray());
                Assert.Equal(4, session.AllocatedBufferBytes);
            }
            Assert.Equal(0, session.AllocatedBufferBytes);
            Assert.False(source.IsDisposed);
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await session.ReadPictureAsync(0));
        }

        [Fact]
        public async Task PendingPictureReadExcludesPacketsPicturesAndDisposal()
        {
            FixturePictureContainerFactory factory = new();
            MediaEngine engine = new MediaEngineBuilder().AddContainer(factory).Build();
            await using ShortReadSource source = new([.. "TEST"u8, 11]);
            await using ContainerSession session = await engine.OpenContainerAsync(source);
            TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource proceed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            factory.LastReader!.BeforePictureRead = async token =>
            {
                entered.TrySetResult();
                await proceed.Task.WaitAsync(token);
            };
            Task<MediaBuffer> pending = session.ReadPictureAsync(0).AsTask();
            await entered.Task;
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.ReadPacketAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.ReadPictureAsync(0));
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.DisposeAsync());
            proceed.SetResult();
            using MediaBuffer picture = await pending;
            Assert.Equal(new byte[] { 7, 8, 9 }, picture.Memory.ToArray());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task CancellationAndReaderFailureFaultSessionWithoutLeakingStorage(bool cancel)
        {
            FixturePictureContainerFactory factory = new();
            MediaEngine engine = new MediaEngineBuilder().AddContainer(factory).Build();
            await using ShortReadSource source = new([.. "TEST"u8, 11]);
            await using ContainerSession session = await engine.OpenContainerAsync(source);
            using CancellationTokenSource cancellation = new();
            TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            factory.LastReader!.BeforePictureRead = async token =>
            {
                entered.TrySetResult();
                if (cancel)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                throw new InvalidDataException("The picture data is truncated.");
            };
            Task<MediaBuffer> pending = session.ReadPictureAsync(0, cancellation.Token).AsTask();
            await entered.Task;
            if (cancel)
            {
                await cancellation.CancelAsync();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            }
            else
            {
                await Assert.ThrowsAsync<InvalidDataException>(() => pending);
            }
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.ReadPictureAsync(0));
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.ReadPacketAsync());
            await session.DisposeAsync();
            Assert.Equal(0, session.AllocatedBufferBytes);
            Assert.False(source.IsDisposed);
        }
    }
}

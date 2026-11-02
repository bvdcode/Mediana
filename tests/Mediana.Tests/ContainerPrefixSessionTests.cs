// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class ContainerPrefixSessionTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task PrefixReadsAreExactAndPreserveTheUnwrittenTail(bool attachment)
        {
            FixturePictureContainerFactory factory = new();
            MediaEngine engine = new MediaEngineBuilder().AddContainer(factory).Build();
            await using ShortReadSource source = new([.. "TEST"u8, 11]);
            await using ContainerSession session = await engine.OpenContainerAsync(source);
            foreach (int capacity in new[] { 0, 1, 3, 5 })
            {
                byte[] destination = new byte[capacity];
                destination.AsSpan().Fill(99);
                int count = attachment ? await session.ReadAttachmentPrefixAsync(0, destination) : await session.ReadPicturePrefixAsync(0, destination);
                Assert.Equal(Math.Min(3, capacity), count);
                Assert.Equal(new byte[] { 7, 8, 9 }.AsSpan(0, count).ToArray(), destination.AsSpan(0, count).ToArray());
                Assert.All(destination.Skip(count), value => Assert.Equal(99, value));
            }
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await session.ReadPicturePrefixAsync(-1, new byte[8]));
            using Core.MediaPacket? packet = await session.ReadPacketAsync();
            Assert.NotNull(packet);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(0)]
        [InlineData(2)]
        [InlineData(4)]
        public async Task AReaderReturningAnIncorrectPrefixCountFaultsTheSession(int count)
        {
            FixturePictureContainerFactory factory = new();
            MediaEngine engine = new MediaEngineBuilder().AddContainer(factory).Build();
            await using ShortReadSource source = new([.. "TEST"u8, 11]);
            await using ContainerSession session = await engine.OpenContainerAsync(source);
            factory.LastReader!.PrefixCountOverride = count;
            await Assert.ThrowsAsync<InvalidDataException>(async () => await session.ReadAttachmentPrefixAsync(0, new byte[3]));
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.ReadPacketAsync());
            await session.DisposeAsync();
            Assert.Equal(0, session.AllocatedBufferBytes);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ConcurrentOperationsAreRejectedAndCancellationFaultsTheSession(bool attachment)
        {
            FixturePictureContainerFactory factory = new();
            MediaEngine engine = new MediaEngineBuilder().AddContainer(factory).Build();
            await using ShortReadSource source = new([.. "TEST"u8, 11]);
            await using ContainerSession session = await engine.OpenContainerAsync(source);
            TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenSource cancellation = new();
            factory.LastReader!.BeforePictureRead = async token =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            };
            factory.LastReader.BeforeAttachmentRead = factory.LastReader.BeforePictureRead;
            Task<int> pending = attachment ? session.ReadAttachmentPrefixAsync(0, new byte[3], cancellation.Token).AsTask()
                : session.ReadPicturePrefixAsync(0, new byte[3], cancellation.Token).AsTask();
            await entered.Task;
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.ReadPacketAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.DisposeAsync());
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.ReadPicturePrefixAsync(0, new byte[3]));
            await session.DisposeAsync();
            Assert.Equal(0, session.AllocatedBufferBytes);
        }
    }
}

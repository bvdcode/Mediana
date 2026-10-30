// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.IO;

namespace Mediana.Tests
{
    public class ContainerSessionTests
    {
        [Fact]
        public async Task SequentialProbeReplayNeedsNoDecoderAndRetainedPacketsSurviveDisposal()
        {
            FixtureContainerFactory factory = new();
            MediaEngine engine = new MediaEngineBuilder().AddContainer(factory).Build();
            await using ShortReadSource source = new([.. "TEST"u8, 11, 22]);
            ContainerSession session = await engine.OpenContainerAsync(source);
            MediaPacket retained = Assert.IsType<MediaPacket>(await session.ReadPacketAsync());
            Assert.Equal(11, retained.Data.Span[0]);
            using (MediaPacket next = Assert.IsType<MediaPacket>(await session.ReadPacketAsync()))
            {
                Assert.Equal(22, next.Data.Span[0]);
            }
            Assert.Null(await session.ReadPacketAsync());
            await session.DisposeAsync();
            await session.DisposeAsync();
            Assert.True(factory.LastReader!.IsDisposed);
            Assert.False(source.IsDisposed);
            Assert.Equal(11, retained.Data.Span[0]);
            Assert.Equal(1, session.AllocatedBufferBytes);
            retained.Dispose();
            Assert.Equal(0, session.AllocatedBufferBytes);
        }

        [Fact]
        public async Task RandomAccessProbingAndPacketReadingPreserveCallerCursor()
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new FixtureContainerFactory()).Build();
            using MemoryStream stream = new([.. "TEST"u8, 11, 22]);
            await using RandomAccessStreamByteSource source = new(stream, leaveOpen: true);
            await source.ReadAsync(new byte[2]);
            await using (ContainerSession session = await engine.OpenContainerAsync(source))
            using (MediaPacket packet = Assert.IsType<MediaPacket>(await session.ReadPacketAsync()))
            {
                Assert.Equal(11, packet.Data.Span[0]);
                Assert.Equal(2, stream.Position);
            }
            Assert.True(stream.CanRead);
            Assert.Equal(2, stream.Position);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ReadLimitAndCancellationFaultTheSessionAndLeaveSourceOpen(bool cancel)
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new FixtureContainerFactory()).Build();
            await using ShortReadSource source = new([.. "TEST"u8, 11, 22]);
            await using ContainerSession session = await engine.OpenContainerAsync(source, new MediaOpenOptions { MaxPacketReads = 1 });
            if (cancel)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await session.ReadPacketAsync(new CancellationToken(true)));
            }
            else
            {
                using MediaPacket packet = Assert.IsType<MediaPacket>(await session.ReadPacketAsync());
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.ReadPacketAsync());
            }
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.ReadPacketAsync());
            await session.DisposeAsync();
            Assert.Equal(0, session.AllocatedBufferBytes);
            Assert.False(source.IsDisposed);
        }

        [Fact]
        public async Task OverlappingReadsAndDisposalAreRejectedWithoutInterruptingThePendingRead()
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new FixtureContainerFactory()).Build();
            await using GatedByteSource source = new([.. "TEST"u8, 11]);
            await using ContainerSession session = await engine.OpenContainerAsync(source);
            source.GateReads = true;
            Task<MediaPacket?> pending = session.ReadPacketAsync().AsTask();
            await source.Entered.Task;
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.ReadPacketAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.DisposeAsync());
            source.Continue.SetResult();
            using MediaPacket packet = Assert.IsType<MediaPacket>(await pending);
            Assert.Equal(11, packet.Data.Span[0]);
        }
    }
}

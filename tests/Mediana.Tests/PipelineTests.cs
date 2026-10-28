using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Execution;

namespace Mediana.Tests
{
    public class PipelineTests
    {
        [Fact]
        public async Task OnePacketMayProduceSeveralFrames()
        {
            MediaBufferAllocator allocator = new(64);
            await using ShortReadSource source = new([7]);
            await using FixtureContainer reader = new(source, allocator);
            using VideoDecodePipeline pipeline = new(reader, new BurstDecoder(allocator), 0, 3);
            using VideoFrame? first = await pipeline.ReadAsync();
            using VideoFrame? second = await pipeline.ReadAsync();
            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.Equal(0, first.Timestamp!.Value.Value);
            Assert.Equal(1, second.Timestamp!.Value.Value);
            Assert.Null(await pipeline.ReadAsync());
            first.Dispose();
            second.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task InvalidCodecProtocolFailsWithoutSpinning(bool refuseInput)
        {
            MediaBufferAllocator allocator = new(64);
            byte[] input = refuseInput ? [7] : [];
            await using ShortReadSource source = new(input);
            await using FixtureContainer reader = new(source, allocator);
            BurstDecoder decoder = new(allocator)
            {
                RefuseInput = refuseInput,
                RequestInputAfterCompletion = !refuseInput
            };
            using VideoDecodePipeline pipeline = new(reader, decoder, 0, 3);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await pipeline.ReadAsync());
            Assert.True(decoder.IsDisposed);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task CancellationInterruptsAwaitedInputAndConcurrentReadsAreRejected()
        {
            MediaBufferAllocator allocator = new(64);
            await using BlockingContainer reader = new();
            BurstDecoder decoder = new(allocator);
            using VideoDecodePipeline pipeline = new(reader, decoder, 0, 3);
            using CancellationTokenSource cancellation = new();
            Task<VideoFrame?> read = pipeline.ReadAsync(cancellation.Token).AsTask();
            await reader.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await pipeline.ReadAsync());
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await read);
            Assert.True(decoder.IsDisposed);
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}

using Mediana.Containers;
using Mediana.Core;

namespace Mediana.Tests
{
    internal class BlockingContainer : IContainerReader
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<EncodedStreamInfo> Streams => [];

        public async ValueTask<MediaPacket?> ReadPacketAsync(CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

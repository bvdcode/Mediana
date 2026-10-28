using Mediana.Core;

namespace Mediana.Containers
{
    /// <summary>A configured muxer. Input packets are borrowed until each call completes.
    /// Dispose aborts unfinished output; only CompleteAsync finalizes the container.</summary>
    public interface IContainerWriter : IAsyncDisposable
    {
        ValueTask WritePacketAsync(MediaPacket packet, CancellationToken cancellationToken = default);
        ValueTask CompleteAsync(CancellationToken cancellationToken = default);
    }
}

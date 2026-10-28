namespace Mediana.IO
{
    /// <summary>Supports reads relative to the source origin without changing the sequential read position.
    /// Reads and disposal must not overlap. A zero read for a nonempty destination means EOF.</summary>
    public interface IRandomAccessByteSource : IByteSource
    {
        ValueTask<int> ReadAtAsync(long offset, Memory<byte> destination, CancellationToken cancellationToken = default);
    }
}

using Mediana.Core.Audio;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Mp3
{
    internal class Mp3BitReservoir : IDisposable
    {
        private readonly WritableMediaBuffer storage;
        private readonly int maxBackBytes;
        private readonly int maxFrameBytes;
        private int retainedOffset;
        private int retainedBytes;
        private bool disposed;
        private bool faulted;

        public Mp3BitReservoir(MediaBufferAllocator allocator, MpegAudioVersion version, int maxFrameBytes)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFrameBytes);
            maxBackBytes = version switch
            {
                MpegAudioVersion.Mpeg1 => 511,
                MpegAudioVersion.Mpeg2 or MpegAudioVersion.Mpeg25 => 255,
                _ => throw new ArgumentOutOfRangeException(nameof(version))
            };
            ArgumentOutOfRangeException.ThrowIfGreaterThan(maxFrameBytes, int.MaxValue / 8 - maxBackBytes);
            this.maxFrameBytes = maxFrameBytes;
            storage = allocator.Allocate(maxBackBytes + maxFrameBytes);
        }

        public int RetainedBytes => retainedBytes;

        /// <summary>The returned bytes remain valid until the next assembly, reset or disposal.
        /// Only the declared main-data bits belong to the frame; the final byte can contain alignment bits.</summary>
        public ReadOnlySpan<byte> Assemble(ReadOnlySpan<byte> slots, int backBytes, int mainDataBits,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted)
            {
                throw new InvalidOperationException("The MP3 bit reservoir requires reset after a failed frame.");
            }
            bool succeeded = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                ArgumentOutOfRangeException.ThrowIfNegative(backBytes);
                ArgumentOutOfRangeException.ThrowIfNegative(mainDataBits);
                if (slots.Length > maxFrameBytes)
                {
                    throw new NotSupportedException("The MP3 main-data slots exceed the configured frame limit.");
                }
                if (backBytes > maxBackBytes || backBytes > retainedBytes)
                {
                    throw new InvalidDataException("The MP3 bit reservoir points to unavailable or previously consumed data.");
                }
                int total = backBytes + slots.Length;
                if (mainDataBits > total * 8)
                {
                    throw new InvalidDataException("The MP3 main data extends past the current frame.");
                }
                Span<byte> buffer = storage.Memory.Span;
                buffer.Slice(retainedOffset + retainedBytes - backBytes, backBytes).CopyTo(buffer);
                slots.CopyTo(buffer[backBytes..]);
                int usedBytes = (mainDataBits + 7) / 8;
                retainedBytes = Math.Min(maxBackBytes, total - usedBytes);
                retainedOffset = total - retainedBytes;
                succeeded = true;
                return buffer[..usedBytes];
            }
            finally
            {
                if (!succeeded)
                {
                    Reset();
                    faulted = true;
                }
            }
        }

        public void Reset()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            storage.Memory.Span.Clear();
            retainedOffset = 0;
            retainedBytes = 0;
            faulted = false;
        }

        public void Dispose()
        {
            if (!disposed)
            {
                disposed = true;
                retainedBytes = 0;
                storage.Dispose();
            }
            GC.SuppressFinalize(this);
        }
    }
}

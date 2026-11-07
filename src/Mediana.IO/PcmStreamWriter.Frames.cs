// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;
using Mediana.Core.Memory;

namespace Mediana.IO
{
    public static partial class PcmStreamWriter
    {
        /// <summary>Streams a stable interleaved PCM format. The callback transfers each frame's ownership.
        /// Borrows the destination and allocator. Empty input returns no format; failed output must be discarded.</summary>
        public static async Task<PcmStreamWriteResult> WriteFramesAsync(Func<CancellationToken, ValueTask<AudioFrame?>> read,
            Stream destination, MediaBufferAllocator allocator, PcmByteOrder byteOrder = PcmByteOrder.Native,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(read);
            ArgumentNullException.ThrowIfNull(destination);
            ArgumentNullException.ThrowIfNull(allocator);
            if (!Enum.IsDefined(byteOrder)) { throw new ArgumentOutOfRangeException(nameof(byteOrder)); }
            if (!destination.CanWrite) { throw new ArgumentException("The PCM destination must be writable.", nameof(destination)); }
            long samples = 0, bytes = 0;
            AudioFormat? format = null;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using AudioFrame? frame = await read(cancellationToken).ConfigureAwait(false);
                if (frame is null) { break; }
                AudioFormat actual = frame.Format;
                if (actual.Layout != AudioStorageLayout.Interleaved)
                { throw new NotSupportedException("Raw PCM output requires interleaved samples."); }
                if (format is not null && (format.SampleRate != actual.SampleRate || format.SampleFormat != actual.SampleFormat ||
                    !format.Channels.SequenceEqual(actual.Channels)))
                { throw new NotSupportedException("Raw PCM output requires one stable audio format."); }
                format = actual;
                switch (byteOrder)
                {
                    case PcmByteOrder.Native:
                        await destination.WriteAsync(frame.GetPlane(0), cancellationToken).ConfigureAwait(false); break;
                    case PcmByteOrder.LittleEndian:
                        await WriteLittleEndianAsync(destination, frame, allocator, cancellationToken).ConfigureAwait(false); break;
                    case PcmByteOrder.BigEndian:
                        await WriteBigEndianAsync(destination, frame, allocator, cancellationToken).ConfigureAwait(false); break;
                    default: throw new ArgumentOutOfRangeException(nameof(byteOrder));
                }
                samples = checked(samples + frame.SamplesPerChannel);
                bytes = checked(bytes + frame.GetPlane(0).Length);
            }
            return new(samples, format, bytes);
        }
    }
}

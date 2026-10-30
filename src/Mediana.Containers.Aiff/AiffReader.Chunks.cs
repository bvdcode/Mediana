// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Containers.Aiff
{
    public partial class AiffReader
    {
        private async ValueTask ReadCommonAsync(uint size, CancellationToken cancellationToken)
        {
            if (Streams.Count != 0 || size < 18)
            {
                throw new InvalidDataException("The AIFF common chunk is duplicated or truncated.");
            }
            if (size > options.MaxCommonBytes)
            {
                throw new NotSupportedException("The AIFF common chunk exceeds the configured size limit.");
            }
            CountMetadata(size);
            using WritableMediaBuffer common = allocator.Allocate(checked((int)size + 4));
            (compressed ? "AIFC"u8 : "AIFF"u8).CopyTo(common.Memory.Span);
            await ReadAsync(common.Memory[4..], cancellationToken).ConfigureAwait(false);
            Configuration = PcmConfiguration.ReadAiffFormat(common.Memory.Span, out string? reason)
                ?? throw new NotSupportedException(reason);
            SamplesPerChannel = BinaryPrimitives.ReadUInt32BigEndian(common.Memory.Span[6..]);
            Streams = Array.AsReadOnly(new[] { new EncodedStreamInfo(0, MediaKind.Audio, PcmConfiguration.AiffCodecId,
                new TimeBase(1, Configuration.Format.SampleRate), common.Memory.Span, codedDuration: Duration) });
            await ReadPaddingAsync(size, cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask ReadVersionAsync(uint size, CancellationToken cancellationToken)
        {
            if (versionRead || size != 4)
            {
                throw new InvalidDataException("The AIFF-C version chunk is duplicated or has an invalid size.");
            }
            CountMetadata(size);
            await ReadAsync(scratch.Memory[..4], cancellationToken).ConfigureAwait(false);
            if (BinaryPrimitives.ReadUInt32BigEndian(scratch.Memory.Span) != AifcVersion)
            {
                throw new NotSupportedException("The AIFF-C format version is not supported.");
            }
            versionRead = true;
        }

        private async ValueTask ReadSoundHeaderAsync(uint size, CancellationToken cancellationToken)
        {
            if (Streams.Count == 0 || (compressed && !versionRead))
            {
                throw new NotSupportedException("Sequential AIFF reading requires COMM and AIFF-C FVER before SSND.");
            }
            if (size < 8)
            {
                throw new InvalidDataException("The AIFF sound header is truncated.");
            }
            CountMetadata(8);
            await ReadAsync(scratch.Memory[..8], cancellationToken).ConfigureAwait(false);
            uint offset = BinaryPrimitives.ReadUInt32BigEndian(scratch.Memory.Span);
            soundRemaining = SamplesPerChannel * Configuration.BlockAlignment;
            if (offset > size - 8 || soundRemaining > size - 8 - offset)
            {
                throw new InvalidDataException("The AIFF sample count or sound offset exceeds the sound chunk.");
            }
            soundTail = size - 8 - offset - soundRemaining;
            soundChunkSize = size;
            await SkipMetadataAsync(offset, cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask<(uint Id, uint Size)> ReadChunkAsync(CancellationToken cancellationToken)
        {
            if (chunks == options.MaxChunks)
            {
                throw new NotSupportedException("The AIFF chunk count exceeds the configured limit.");
            }
            chunks++;
            CountMetadata(8);
            await ReadAsync(scratch.Memory[..8], cancellationToken).ConfigureAwait(false);
            uint id = BinaryPrimitives.ReadUInt32BigEndian(scratch.Memory.Span);
            uint size = BinaryPrimitives.ReadUInt32BigEndian(scratch.Memory.Span[4..]);
            if ((long)size + (size & 1) > remaining)
            {
                throw new InvalidDataException("An AIFF chunk exceeds its FORM extent.");
            }
            return (id, size);
        }

        private async ValueTask ReadPaddingAsync(uint chunkSize, CancellationToken cancellationToken)
        {
            if ((chunkSize & 1) != 0)
            {
                CountMetadata(1);
                await ReadAsync(scratch.Memory[..1], cancellationToken).ConfigureAwait(false);
                if (scratch.Memory.Span[0] != 0)
                {
                    throw new InvalidDataException("An AIFF chunk padding byte must be zero.");
                }
            }
        }

        private async ValueTask ReadAsync(Memory<byte> destination, CancellationToken cancellationToken)
        {
            if (destination.Length > remaining)
            {
                throw new InvalidDataException("AIFF data exceeds its FORM extent.");
            }
            await source.ReadExactlyAsync(destination, cancellationToken).ConfigureAwait(false);
            remaining -= destination.Length;
        }

        private void CountMetadata(long length)
        {
            if (length > options.MaxMetadataBytes - metadataBytes)
            {
                throw new NotSupportedException("AIFF metadata exceeds the configured byte limit.");
            }
            metadataBytes += length;
        }

        private async ValueTask SkipMetadataAsync(long length, CancellationToken cancellationToken)
        {
            CountMetadata(length);
            while (length != 0)
            {
                int bytes = (int)Math.Min(length, scratch.Memory.Length);
                await ReadAsync(scratch.Memory[..bytes], cancellationToken).ConfigureAwait(false);
                length -= bytes;
            }
        }
    }
}

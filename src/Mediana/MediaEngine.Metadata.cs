// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;

namespace Mediana
{
    public partial class MediaEngine
    {
        private async ValueTask<MediaInfo> ReadContainerInfoAsync(IContainerReader container, MediaBufferAllocator allocator,
            int maximumProbeBytes, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Dictionary<int, StreamMetadata> metadata = [];
            foreach (EncodedStreamInfo stream in container.Streams)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!metadataReaders.TryGetValue(stream.CodecId, out IStreamMetadataReader? reader))
                {
                    continue;
                }
                StreamMetadata? value = reader.Read(stream);
                if (value is null && reader is IPacketPrefixMetadataReader packetReader &&
                    container is IIndexedPacketPrefixReader indexed && indexed.GetPacketCount(stream.StreamIndex) != 0)
                {
                    value = await ReadPacketMetadataAsync(indexed, packetReader, stream, allocator,
                        maximumProbeBytes, cancellationToken).ConfigureAwait(false);
                }
                if (value is not null)
                {
                    if (value.Kind != stream.Kind)
                    {
                        throw new InvalidOperationException("The metadata reader returned a different media kind.");
                    }
                    metadata.Add(stream.StreamIndex, value);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new MediaInfo(container.Info, container.Streams, metadata);
        }

        private static async ValueTask<StreamMetadata?> ReadPacketMetadataAsync(IIndexedPacketPrefixReader container,
            IPacketPrefixMetadataReader reader, EncodedStreamInfo stream, MediaBufferAllocator allocator,
            int maximumProbeBytes, CancellationToken cancellationToken)
        {
            int length = reader.PacketProbeLength;
            if (length <= 0)
            {
                throw new InvalidOperationException("The metadata reader returned an invalid packet probe length.");
            }
            if (length > maximumProbeBytes)
            {
                throw new NotSupportedException("Codec metadata inspection exceeds the configured probe byte limit.");
            }
            using WritableMediaBuffer prefix = allocator.Allocate(length);
            int read = await container.ReadPacketPrefixAtAsync(stream.StreamIndex, 0, prefix.Memory, cancellationToken).ConfigureAwait(false);
            if (read < 0 || read > length)
            {
                throw new InvalidDataException("The container returned an invalid packet prefix length.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            return reader.ReadPacketPrefix(stream, prefix.Span[..read]);
        }
    }
}

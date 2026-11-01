// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Containers.Matroska
{
    internal static class MatroskaPacketIndexBuilder
    {
        public static async ValueTask<MatroskaPacketIndex> ReadAsync(IRandomAccessByteSource source,
            MediaBufferAllocator allocator, EbmlElement segment, IReadOnlyList<MatroskaTrack> tracks,
            MatroskaIndexOptions options, MatroskaMetadataLimits metadataLimits, int maximumSizeWidth = 8,
            CancellationToken token = default)
        {
            options.Validate();
            if (segment.Id != 0x18538067)
            {
                throw new ArgumentException("A Matroska Segment is required.", nameof(segment));
            }
            token.ThrowIfCancellationRequested();
            using MatroskaSegmentReader segments = new(source, allocator, maximumSizeWidth);
            using MatroskaMetadataReader metadata = new(source, allocator, metadataLimits, maximumSizeWidth);
            using MatroskaBlockReader blocks = new(source, allocator, options.MaximumBlockHeaderBytes, options.MaximumFrameBytes);
            MatroskaPacketIndex index = new(allocator, tracks, options);
            bool success = false;
            try
            {
                long offset = segment.DataOffset;
                while (await segments.ReadChildAsync(segment, offset, token).ConfigureAwait(false) is EbmlElement child)
                {
                    if (child.Id == 0x1F43B675)
                    {
                        await ReadClusterAsync(metadata, blocks, index, child, options.VerifyClusterChecksums, token).ConfigureAwait(false);
                    }
                    offset = child.EndOffset!.Value;
                }
                await segments.VerifyDocumentEndAsync(segment, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                index.Complete();
                success = true;
                return index;
            }
            finally
            {
                if (!success)
                {
                    index.Dispose();
                }
            }
        }

        private static async ValueTask ReadClusterAsync(MatroskaMetadataReader metadata, MatroskaBlockReader blocks,
            MatroskaPacketIndex index, EbmlElement cluster, bool verifyChecksum, CancellationToken token)
        {
            ulong? timestamp = null;
            long offset = cluster.DataOffset;
            while (await metadata.ReadChildAsync(cluster, offset, token, verifyChecksum).ConfigureAwait(false) is EbmlElement child)
            {
                switch (child.Id)
                {
                    case 0xE7:
                        if (timestamp is not null)
                        {
                            throw new InvalidDataException("A Matroska Cluster has duplicate timestamps.");
                        }
                        timestamp = await metadata.ReadUnsignedAsync(child, 0, token).ConfigureAwait(false);
                        break;
                    case 0xA3:
                        MatroskaBlockHeader simple = await blocks.ReadAsync(child, token).ConfigureAwait(false);
                        index.AddBlock(child, simple, false, default);
                        break;
                    case 0xA0:
                        MatroskaBlockGroup group = await MatroskaBlockGroupReader.ReadAsync(metadata, child, token).ConfigureAwait(false);
                        MatroskaBlockHeader header = await blocks.ReadAsync(group.Block, token).ConfigureAwait(false);
                        index.AddBlock(group.Block, header, group.HasReferences, group.Extensions);
                        break;
                }
                offset = child.EndOffset!.Value;
            }
            if (timestamp is not ulong required)
            {
                throw new InvalidDataException("A Matroska Cluster requires a Timestamp.");
            }
            index.AddCluster(required);
        }
    }
}

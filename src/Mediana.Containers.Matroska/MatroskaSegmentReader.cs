// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Containers.Matroska
{
    /// <summary>Traverses Segment boundaries on a known-length random-access source.
    /// Borrows the source; operations and disposal must not overlap.</summary>
    internal class MatroskaSegmentReader : IDisposable
    {
        private const uint SegmentId = 0x18538067;
        private const uint ClusterId = 0x1F43B675;
        private const uint EbmlId = 0x1A45DFA3;
        private const uint VoidId = 0xEC;
        private readonly EbmlElementReader reader;
        private readonly long inputEnd;
        private readonly MatroskaElementBudget? elementBudget;

        public MatroskaSegmentReader(IRandomAccessByteSource source, MediaBufferAllocator allocator, int maximumSizeWidth,
            MatroskaElementBudget? elementBudget = null)
        {
            ArgumentNullException.ThrowIfNull(source);
            this.elementBudget = elementBudget;
            inputEnd = source.Length ?? throw new NotSupportedException("Matroska indexing requires a known source length.");
            reader = new(source, allocator, maximumSizeWidth: maximumSizeWidth);
        }

        public async ValueTask<EbmlElement> ReadRootAsync(long offset, CancellationToken cancellationToken = default)
        {
            while (true)
            {
                EbmlElement root = await ReadElementAsync(offset, inputEnd, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidDataException("The Matroska Segment is missing.");
                if (root.Id == SegmentId)
                {
                    return root;
                }
                if (root.Id != VoidId || root.EndOffset is not long end)
                {
                    throw new InvalidDataException("The Matroska EBML header must be followed by a Segment.");
                }
                offset = end;
            }
        }

        public async ValueTask<EbmlElement?> ReadChildAsync(EbmlElement segment, long offset,
            CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(offset, segment.DataOffset);
            long limit = segment.EndOffset ?? inputEnd;
            EbmlElement? next = await ReadElementAsync(offset, limit, cancellationToken).ConfigureAwait(false);
            if (next is not EbmlElement child)
            {
                return null;
            }
            if (child.Id == EbmlId && segment.EndOffset is null)
            {
                throw new NotSupportedException("Multiple Matroska documents in one source are not supported.");
            }
            if (child.Id is SegmentId or EbmlId || (child.Id < 0x10000000 && child.Id is not (VoidId or 0xBF)))
            {
                throw new InvalidDataException("The element is not valid at Matroska Segment level.");
            }
            if (child.EndOffset is not null)
            {
                return child;
            }
            if (child.Id != ClusterId)
            {
                throw new InvalidDataException("Only Matroska Segment and Cluster may have unknown lengths.");
            }
            long end = await FindClusterEndAsync(child.DataOffset, limit, cancellationToken).ConfigureAwait(false);
            return child with { EndOffset = end };
        }

        public async ValueTask VerifyDocumentEndAsync(EbmlElement segment, CancellationToken cancellationToken = default)
        {
            long offset = segment.EndOffset ?? inputEnd;
            while (true)
            {
                EbmlElement? next = await ReadElementAsync(offset, inputEnd, cancellationToken).ConfigureAwait(false);
                if (next is not EbmlElement root)
                {
                    return;
                }
                if (root.Id == EbmlId)
                {
                    throw new NotSupportedException("Multiple Matroska documents in one source are not supported.");
                }
                if (root.Id != VoidId || root.EndOffset is not long end)
                {
                    throw new InvalidDataException("An unexpected element follows the Matroska Segment.");
                }
                offset = end;
            }
        }

        private async ValueTask<long> FindClusterEndAsync(long offset, long parentEnd, CancellationToken cancellationToken)
        {
            while (true)
            {
                EbmlElement? next = await ReadElementAsync(offset, parentEnd, cancellationToken).ConfigureAwait(false);
                if (next is not EbmlElement child || IsBoundary(child.Id))
                {
                    return offset;
                }
                if (child.EndOffset is not long end)
                {
                    throw new InvalidDataException("Matroska Cluster children must have known lengths.");
                }
                offset = end;
            }
        }

        private async ValueTask<EbmlElement?> ReadElementAsync(long offset, long end, CancellationToken token)
        {
            EbmlElement? element = await reader.ReadAsync(offset, end, token).ConfigureAwait(false);
            if (element is not null)
            {
                elementBudget?.Reserve();
            }
            return element;
        }

        private static bool IsBoundary(uint id) => id switch
        {
            SegmentId or EbmlId or ClusterId or 0x114D9B74 or 0x1549A966 or 0x1654AE6B or
                0x1C53BB6B or 0x1941A469 or 0x1043A770 or 0x1254C367 => true,
            _ => false
        };

        public void Dispose()
        {
            reader.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}

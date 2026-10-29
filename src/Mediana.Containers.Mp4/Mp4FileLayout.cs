// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Containers.Mp4
{
    internal class Mp4FileLayout(MediaBuffer metadata, IReadOnlyList<Mp4DataRange> dataRanges) : IDisposable
    {
        public MediaBuffer Metadata { get; } = metadata;
        public IReadOnlyList<Mp4DataRange> DataRanges { get; } = dataRanges;

        public static async ValueTask<Mp4FileLayout> ReadAsync(IRandomAccessByteSource source,
            MediaBufferAllocator allocator, Mp4ReadOptions options, Mp4ParseBudget budget,
            CancellationToken cancellationToken)
        {
            long length = source.Length ?? throw new NotSupportedException("MP4 reading requires a known source length.");
            if (length < 8)
            {
                throw new InvalidDataException("The MP4 file is too short.");
            }
            using WritableMediaBuffer header = allocator.Allocate(16);
            MediaBuffer? movie = null;
            List<Mp4DataRange> ranges = [];
            long offset = 0;
            bool transferred = false;
            try
            {
                while (offset < length)
                {
                    budget.TakeBox();
                    if (length - offset < 8)
                    {
                        throw new InvalidDataException("The final MP4 box header is truncated.");
                    }
                    await source.ReadExactlyAtAsync(offset, header.Memory[..8], cancellationToken).ConfigureAwait(false);
                    uint shortSize = Mp4Binary.UInt32(header.Memory.Span, 0);
                    string type = Mp4Binary.FourCc(header.Memory.Span, 4);
                    int headerSize = 8;
                    long size;
                    if (shortSize == 1)
                    {
                        if (length - offset < 16)
                        {
                            throw new InvalidDataException("The extended MP4 box header is truncated.");
                        }
                        await source.ReadExactlyAtAsync(offset + 8, header.Memory[8..], cancellationToken).ConfigureAwait(false);
                        size = Mp4Binary.Offset64(Mp4Binary.UInt64(header.Memory.Span, 8));
                        headerSize = 16;
                    }
                    else
                    {
                        size = shortSize == 0 ? length - offset : shortSize;
                    }
                    if (type == "uuid")
                    {
                        headerSize += 16;
                    }
                    if (size < headerSize || size > length - offset)
                    {
                        throw new InvalidDataException($"Invalid MP4 '{type}' box at offset {offset}.");
                    }
                    switch (type)
                    {
                        case "moov":
                            if (movie is not null)
                            {
                                throw new InvalidDataException("The MP4 movie box is duplicated.");
                            }
                            long payloadLength = size - headerSize;
                            if (payloadLength == 0 || payloadLength > options.MaxMetadataBytes)
                            {
                                throw new NotSupportedException("The MP4 metadata size exceeds the supported range.");
                            }
                            using (WritableMediaBuffer writable = allocator.Allocate((int)payloadLength))
                            {
                                await source.ReadExactlyAtAsync(offset + headerSize, writable.Memory, cancellationToken).ConfigureAwait(false);
                                movie = writable.Publish();
                            }
                            break;
                        case "mdat":
                            ranges.Add(new Mp4DataRange(offset + headerSize, size - headerSize));
                            break;
                        case "moof":
                            throw new NotSupportedException("Fragmented MP4 is not supported.");
                    }
                    offset += size;
                }
                if (movie is null || ranges.Count == 0)
                {
                    throw new InvalidDataException("The MP4 file requires movie and media data boxes.");
                }
                Mp4FileLayout layout = new(movie, ranges.AsReadOnly());
                transferred = true;
                return layout;
            }
            finally
            {
                if (!transferred)
                {
                    movie?.Dispose();
                }
            }
        }

        public void Dispose()
        {
            Metadata.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}

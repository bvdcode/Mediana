// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Containers.Mp4
{
    internal class Mp4FileLayout(MediaBuffer metadata, int movieLength, IReadOnlyList<Mp4MovieFragment> fragments,
        IReadOnlyList<Mp4DataRange> dataRanges, string? majorBrand, bool quickTimeCompatible) : IDisposable
    {
        public MediaBuffer Metadata { get; } = metadata;
        public ReadOnlyMemory<byte> Movie => Metadata.Memory[..movieLength];
        public IReadOnlyList<Mp4MovieFragment> Fragments { get; } = fragments;
        public IReadOnlyList<Mp4DataRange> DataRanges { get; } = dataRanges;
        public string? MajorBrand { get; } = majorBrand;
        public bool QuickTimeCompatible { get; } = quickTimeCompatible;

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
            Mp4DataRange? movie = null;
            List<(long Offset, Mp4DataRange Payload)> fragments = [];
            int metadataBytes = 0;
            string? majorBrand = null;
            bool quickTimeCompatible = false;
            List<Mp4DataRange> ranges = [];
            long offset = 0;

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
                    case "ftyp":
                        long brandBytes = size - headerSize;
                        if (majorBrand is not null || brandBytes < 8 || (brandBytes - 8) % 4 != 0)
                        {
                            throw new InvalidDataException("The MP4 file type box is duplicated or malformed.");
                        }
                        if (brandBytes > options.MaxMetadataBytes)
                        {
                            throw new NotSupportedException("The MP4 file type box exceeds the configured metadata byte limit.");
                        }
                        using (WritableMediaBuffer brands = allocator.Allocate((int)brandBytes))
                        {
                            await source.ReadExactlyAtAsync(offset + headerSize, brands.Memory, cancellationToken).ConfigureAwait(false);
                            majorBrand = Mp4Binary.FourCc(brands.Memory.Span, 0);
                            quickTimeCompatible = majorBrand == "qt  ";
                            for (int index = 8; index < brands.Memory.Length; index += 4)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                if (brands.Memory.Span.Slice(index, 4).SequenceEqual("qt  "u8))
                                {
                                    quickTimeCompatible = true;
                                }
                            }
                        }
                        break;
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
                        metadataBytes = AddMetadataBytes(metadataBytes, payloadLength, options);
                        movie = new Mp4DataRange(offset + headerSize, payloadLength);
                        break;
                    case "mdat":
                        ranges.Add(new Mp4DataRange(offset + headerSize, size - headerSize));
                        break;
                    case "moof":
                        metadataBytes = AddMetadataBytes(metadataBytes, size - headerSize, options);
                        fragments.Add((offset, new Mp4DataRange(offset + headerSize, size - headerSize)));
                        break;
                }
                offset += size;
            }
            if (movie is null)
            {
                throw new InvalidDataException("The MP4 file requires a movie box.");
            }
            using WritableMediaBuffer writable = allocator.Allocate(metadataBytes);
            int movieLength = (int)movie.Value.Length;
            await source.ReadExactlyAtAsync(movie.Value.Offset, writable.Memory[..movieLength], cancellationToken).ConfigureAwait(false);
            Mp4MovieFragment[] headers = new Mp4MovieFragment[fragments.Count];
            int destination = movieLength;
            for (int index = 0; index < fragments.Count; index++)
            {
                (long fragmentOffset, Mp4DataRange payload) = fragments[index];
                Memory<byte> bytes = writable.Memory.Slice(destination, (int)payload.Length);
                await source.ReadExactlyAtAsync(payload.Offset, bytes, cancellationToken).ConfigureAwait(false);
                headers[index] = new(fragmentOffset, bytes);
                destination += bytes.Length;
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new(writable.Publish(), movieLength, headers, ranges.AsReadOnly(), majorBrand, quickTimeCompatible);
        }

        private static int AddMetadataBytes(int total, long length, Mp4ReadOptions options)
        {
            if (length < 0 || length > options.MaxMetadataBytes - total)
            {
                throw new NotSupportedException("The MP4 metadata size exceeds the configured byte limit.");
            }
            return total + (int)length;
        }

        public void Dispose()
        {
            Metadata.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Mp4
{
    internal class Mp4SampleTable
    {
        private readonly ReadOnlyMemory<byte> sizes;
        private readonly uint constantSize;
        private readonly Mp4Table chunks;
        private readonly bool wideOffsets;

        public Mp4SampleTable(Mp4BoxCollection boxes, Mp4ReadOptions options,
            IReadOnlyList<Mp4DataRange> dataRanges, CancellationToken cancellationToken)
        {
            ReadOnlyMemory<byte> sizePayload = boxes.Required("stsz").Payload;
            Mp4Binary.FullBox(sizePayload.Span);
            constantSize = Mp4Binary.UInt32(sizePayload.Span, 4);
            uint sampleCount = Mp4Binary.UInt32(sizePayload.Span, 8);
            if (sampleCount > options.MaxSamplesPerTrack)
            {
                throw new NotSupportedException("The MP4 sample count exceeds the configured limit.");
            }
            SampleCount = (int)sampleCount;
            long sizeBytes = constantSize == 0 ? (long)SampleCount * 4 : 0;
            if (12 + sizeBytes != sizePayload.Length)
            {
                throw new InvalidDataException("The MP4 sample size count does not match its box size.");
            }
            sizes = sizePayload[12..];
            Mp4Box? narrow = boxes.Optional("stco");
            Mp4Box? wide = boxes.Optional("co64");
            if ((narrow is null) == (wide is null))
            {
                throw new InvalidDataException("MP4 requires exactly one chunk offset table.");
            }
            wideOffsets = wide is not null;
            chunks = new Mp4Table((wide ?? narrow)!.Value.Payload, wideOffsets ? 8 : 4);
            ChunkRuns = new Mp4Table(boxes.Required("stsc").Payload, 12);
            Timing = new Mp4Table(boxes.Required("stts").Payload, 8);
            Mp4Box? composition = boxes.Optional("ctts");
            if (composition is not null)
            {
                byte version = Mp4Binary.Range(composition.Value.Payload.Span, 0, 1)[0];
                if (version > 1)
                {
                    throw new NotSupportedException("The MP4 composition offset version is not supported.");
                }
                SignedComposition = version == 1;
                Composition = new Mp4Table(composition.Value.Payload, 8, version);
            }
            Mp4Box? sync = boxes.Optional("stss");
            SyncSamples = sync is null ? null : new Mp4Table(sync.Value.Payload, 4);
            ValidateRuns(Timing, cancellationToken);
            if (Composition is not null)
            {
                ValidateRuns(Composition, cancellationToken);
            }
            ValidateSyncSamples(cancellationToken);
            ValidateChunks(dataRanges, cancellationToken);
        }

        public int SampleCount { get; }
        public int ChunkCount => chunks.Count;
        public Mp4Table ChunkRuns { get; }
        public Mp4Table Timing { get; }
        public Mp4Table? Composition { get; }
        public bool SignedComposition { get; }
        public Mp4Table? SyncSamples { get; }

        public int SampleSize(int index)
        {
            uint size = constantSize == 0 ? Mp4Binary.UInt32(sizes.Span, index * 4) : constantSize;
            if (size == 0 || size > int.MaxValue)
            {
                throw new NotSupportedException("Empty MP4 samples and samples above 2 GiB are not supported.");
            }
            return (int)size;
        }

        public long ChunkOffset(int index) => wideOffsets
            ? Mp4Binary.Offset64(chunks.UInt64(index)) : chunks.UInt32(index);

        public long SampleByteCount(int firstSample, int count, CancellationToken cancellationToken)
        {
            if (constantSize != 0)
            {
                return count == 0 ? 0 : (long)SampleSize(firstSample) * count;
            }
            long bytes = 0;
            for (int index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bytes = checked(bytes + SampleSize(firstSample + index));
            }
            return bytes;
        }

        private void ValidateRuns(Mp4Table table, CancellationToken cancellationToken)
        {
            long count = 0;
            for (int index = 0; index < table.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                uint run = table.UInt32(index);
                if (run == 0)
                {
                    throw new InvalidDataException("An MP4 timing run has no samples.");
                }
                count += run;
                if (count > SampleCount)
                {
                    throw new InvalidDataException("MP4 timing runs exceed the sample count.");
                }
            }
            if (count != SampleCount)
            {
                throw new InvalidDataException("MP4 timing runs do not cover every sample.");
            }
        }

        private void ValidateSyncSamples(CancellationToken cancellationToken)
        {
            uint previous = 0;
            for (int index = 0; index < (SyncSamples?.Count ?? 0); index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                uint sample = SyncSamples!.UInt32(index);
                if (sample <= previous || sample > SampleCount)
                {
                    throw new InvalidDataException("The MP4 sync sample numbers are invalid.");
                }
                previous = sample;
            }
        }

        private void ValidateChunks(IReadOnlyList<Mp4DataRange> ranges, CancellationToken cancellationToken)
        {
            uint previous = 0;
            for (int index = 0; index < ChunkRuns.Count; index++)
            {
                uint first = ChunkRuns.UInt32(index);
                uint count = ChunkRuns.UInt32(index, 4);
                if (first <= previous || first > chunks.Count || (index == 0 && first != 1) || count == 0)
                {
                    throw new InvalidDataException("The MP4 sample-to-chunk mapping is invalid.");
                }
                if (ChunkRuns.UInt32(index, 8) != 1)
                {
                    throw new NotSupportedException("MP4 sample description changes are not supported.");
                }
                previous = first;
            }
            if ((chunks.Count == 0) != (ChunkRuns.Count == 0))
            {
                throw new InvalidDataException("The MP4 chunk mapping is incomplete.");
            }
            int sample = 0;
            int run = 0;
            for (int chunk = 0; chunk < chunks.Count; chunk++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (run + 1 < ChunkRuns.Count && ChunkRuns.UInt32(run + 1) == chunk + 1)
                {
                    run++;
                }
                uint count = ChunkRuns.UInt32(run, 4);
                if (count > SampleCount - sample)
                {
                    throw new InvalidDataException("MP4 chunks exceed the sample count.");
                }
                long bytes = SampleByteCount(sample, (int)count, cancellationToken);
                sample += (int)count;
                long offset = ChunkOffset(chunk);
                if (!ContainsMediaRange(ranges, offset, bytes))
                {
                    throw new InvalidDataException("An MP4 chunk extends outside a media data box.");
                }
            }
            if (sample != SampleCount)
            {
                throw new InvalidDataException("MP4 chunks do not cover every sample.");
            }
        }

        private static bool ContainsMediaRange(IReadOnlyList<Mp4DataRange> ranges, long offset, long length)
        {
            int low = 0;
            int high = ranges.Count - 1;
            while (low <= high)
            {
                int middle = low + (high - low) / 2;
                Mp4DataRange range = ranges[middle];
                if (offset < range.Offset)
                {
                    high = middle - 1;
                }
                else if (offset - range.Offset > range.Length)
                {
                    low = middle + 1;
                }
                else
                {
                    return range.Contains(offset, length);
                }
            }
            return false;
        }
    }
}

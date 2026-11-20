// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Containers.Mp4
{
    internal class Mp4FragmentRun
    {
        private const int CheckpointSamples = 64;
        private readonly ReadOnlyMemory<byte> entries;
        private readonly int stride;
        private readonly int durationField;
        private readonly int sizeField;
        private readonly int flagsField;
        private readonly int compositionField;
        private readonly bool signedComposition;
        private readonly Mp4FragmentDefaults defaults;
        private readonly uint firstFlags;
        private readonly Mp4FragmentCheckpoint[] checkpoints;
        private uint lastDurationExtension;

        public Mp4FragmentRun(ReadOnlyMemory<byte> payload, int firstSample, long decodeTime,
            long baseOffset, long previousEnd, Mp4FragmentDefaults defaults, int remainingSamples,
            IReadOnlyList<Mp4DataRange> dataRanges, CancellationToken token)
        {
            this.defaults = defaults;
            FirstSample = firstSample;
            ReadOnlySpan<byte> data = payload.Span;
            uint header = Mp4Binary.UInt32(data, 0);
            byte version = (byte)(header >> 24);
            uint flags = header & 0xFFFFFF;
            if (version > 1 || (flags & ~0xF05u) != 0)
            {
                throw new NotSupportedException("The MP4 track run version or flags are unsupported.");
            }
            if ((flags & 0x404) == 0x404)
            {
                throw new InvalidDataException("An MP4 track run cannot combine first-sample and per-sample flags.");
            }
            uint count = Mp4Binary.UInt32(data, 4);
            if (count > remainingSamples)
            {
                throw new NotSupportedException("The MP4 sample count exceeds the configured limit.");
            }
            SampleCount = (int)count;
            int offset = 8;
            long dataOffset = previousEnd;
            if ((flags & 1) != 0)
            {
                dataOffset = checked(baseOffset + Mp4Binary.Int32(data, offset));
                offset += 4;
            }
            firstFlags = defaults.Flags;
            if ((flags & 4) != 0)
            {
                firstFlags = Mp4Binary.UInt32(data, offset);
                offset += 4;
            }
            int entryBytes = 0;
            durationField = Field(flags, 0x100, ref entryBytes);
            sizeField = Field(flags, 0x200, ref entryBytes);
            flagsField = Field(flags, 0x400, ref entryBytes);
            compositionField = Field(flags, 0x800, ref entryBytes);
            stride = entryBytes;
            signedComposition = version == 1;
            if ((long)offset + (long)SampleCount * stride != data.Length)
            {
                throw new InvalidDataException("The MP4 track run length differs from its sample count.");
            }
            entries = payload[offset..];
            checkpoints = new Mp4FragmentCheckpoint[SampleCount == 0 ? 0 : (SampleCount - 1) / CheckpointSamples + 1];
            long position = dataOffset;
            long time = decodeTime;
            long minimumPresentation = long.MaxValue;
            ulong syncMask = 0;
            long blockOffset = position;
            long blockTime = time;
            for (int index = 0; index < SampleCount; index++)
            {
                token.ThrowIfCancellationRequested();
                Mp4SampleLocation sample = Read(index, position, time);
                position = checked(position + sample.Size);
                time = checked(time + sample.Duration);
                long composition = checked(sample.PresentationTime - sample.DecodeTime);
                if (index == 0)
                {
                    CompositionOffset = composition;
                }
                else
                {
                    HasVaryingComposition |= composition != CompositionOffset;
                }
                minimumPresentation = Math.Min(minimumPresentation, sample.PresentationTime);
                PresentationEnd = Math.Max(PresentationEnd, checked(sample.PresentationTime + sample.Duration));
                DecodeTimeLeadTicks = Math.Max(DecodeTimeLeadTicks, checked(sample.DecodeTime - sample.PresentationTime));
                if (sample.IsSync)
                {
                    syncMask |= 1UL << (index % CheckpointSamples);
                }
                if (index % CheckpointSamples == CheckpointSamples - 1 || index == SampleCount - 1)
                {
                    checkpoints[index / CheckpointSamples] = new(blockOffset, blockTime, minimumPresentation, syncMask);
                    blockOffset = position;
                    blockTime = time;
                    minimumPresentation = long.MaxValue;
                    syncMask = 0;
                }
            }
            PayloadBytes = checked(position - dataOffset);
            DecodeDurationTicks = checked(time - decodeTime);
            EndOffset = position;
            EndDecodeTime = time;
            if (SampleCount != 0 && !Mp4MediaRanges.Contains(dataRanges, dataOffset, PayloadBytes))
            {
                throw new InvalidDataException("An MP4 fragment run extends outside a media data box.");
            }
        }

        public long CompositionOffset { get; }
        public bool HasVaryingComposition { get; }
        public int FirstSample { get; }
        public int SampleCount { get; }
        public long PayloadBytes { get; }
        public long DecodeDurationTicks { get; private set; }
        public long DecodeTimeLeadTicks { get; }
        public long EndOffset { get; }
        public long PresentationEnd { get; private set; }
        public long EndDecodeTime { get; private set; }

        public void ExtendLastDuration(long extension)
        {
            Mp4SampleLocation last = Locate(SampleCount - 1);
            _ = checked((uint)(last.Duration + extension));
            lastDurationExtension = checked(lastDurationExtension + (uint)extension);
            DecodeDurationTicks = checked(DecodeDurationTicks + extension);
            EndDecodeTime = checked(EndDecodeTime + extension);
            PresentationEnd = Math.Max(PresentationEnd, checked(last.PresentationTime + last.Duration + extension));
        }

        public Mp4SampleLocation Locate(int sample)
        {
            Mp4FragmentCheckpoint checkpoint = checkpoints[sample / CheckpointSamples];
            long offset = checkpoint.Offset;
            long time = checkpoint.DecodeTime;
            for (int index = sample / CheckpointSamples * CheckpointSamples; index < sample; index++)
            {
                offset += Size(index);
                time += Duration(index);
            }
            return Read(sample, offset, time);
        }

        public Mp4SampleLocation Read(int sample, long offset, long decodeTime)
        {
            long composition = 0;
            if (compositionField >= 0)
            {
                int position = sample * stride + compositionField;
                composition = signedComposition ? Mp4Binary.Int32(entries.Span, position) : Mp4Binary.UInt32(entries.Span, position);
            }
            uint flags = Value(sample, flagsField, sample == 0 ? firstFlags : defaults.Flags);
            uint duration = Duration(sample);
            if (sample == SampleCount - 1)
            {
                duration = checked(duration + lastDurationExtension);
            }
            return new(offset, Size(sample), decodeTime, checked(decodeTime + composition), duration, (flags & 0x10000) == 0);
        }

        public int FindSyncSample(int sample)
        {
            int block = sample / CheckpointSamples;
            ulong mask = checkpoints[block].SyncMask & (ulong.MaxValue >> (CheckpointSamples - 1 - sample % CheckpointSamples));
            while (true)
            {
                if (mask != 0)
                {
                    return block * CheckpointSamples + BitOperations.Log2(mask);
                }
                if (--block < 0)
                {
                    return -1;
                }
                mask = checkpoints[block].SyncMask;
            }
        }

        public int FindSeekSample(Int128 target, int beforeSample, CancellationToken token)
        {
            int last = beforeSample - 1;
            while (last >= 0)
            {
                token.ThrowIfCancellationRequested();
                int block = last / CheckpointSamples;
                Mp4FragmentCheckpoint checkpoint = checkpoints[block];
                if (target < checkpoint.MinimumPresentationTime)
                {
                    last = block * CheckpointSamples - 1;
                    continue;
                }
                int candidate = FindSyncSample(last);
                if (candidate < 0)
                {
                    return -1;
                }
                if (Locate(candidate).PresentationTime <= target)
                {
                    return candidate;
                }
                last = candidate - 1;
            }
            return -1;
        }

        private int Size(int sample)
        {
            uint size = Value(sample, sizeField, defaults.Size);
            if (size == 0 || size > int.MaxValue)
            {
                throw new NotSupportedException("Empty MP4 samples and samples above 2 GiB are not supported.");
            }
            return (int)size;
        }

        private uint Duration(int sample) => Value(sample, durationField, defaults.Duration);

        private uint Value(int sample, int field, uint inherited) => field < 0
            ? inherited : Mp4Binary.UInt32(entries.Span, sample * stride + field);

        private static int Field(uint flags, uint mask, ref int stride)
        {
            if ((flags & mask) == 0)
            {
                return -1;
            }
            int field = stride;
            stride += 4;
            return field;
        }
    }
}

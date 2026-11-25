// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core;
using Mediana.Formats;
using Mediana.Formats.Audio;
using Mediana.IO;
using Mediana.Core.Timing;

namespace Mediana.Containers.Matroska
{
    /// <summary>Exact audio clocks from codec configuration or bounded frame prefixes.</summary>
    internal partial class MatroskaAudioTimingIndex : IDisposable
    {
        private readonly WritableMediaBuffer?[] storage;
        private readonly AudioStreamMetadata?[] metadata;

        private MatroskaAudioTimingIndex(int tracks)
        { storage = new WritableMediaBuffer?[tracks]; metadata = new AudioStreamMetadata?[tracks]; }

        public static async ValueTask<MatroskaAudioTimingIndex?> CreateAsync(IRandomAccessByteSource source,
            MediaBufferAllocator allocator, MatroskaPacketIndex index, IReadOnlyList<MatroskaTrack> tracks,
            IReadOnlyList<MatroskaCodecConfiguration> configurations, MatroskaIndexOptions options, CancellationToken token)
        {
            bool required = false;
            foreach (MatroskaCodecConfiguration configuration in configurations)
            {
                if (RequiresTiming(configuration))
                { required = true; break; }
            }
            if (!required) { return null; }
            MatroskaAudioTimingIndex result = new(tracks.Count);
            bool success = false;
            try
            {
                using MatroskaBlockReader blocks = new(source, allocator, options.MaximumBlockHeaderBytes, options.MaximumFrameBytes);
                using WritableMediaBuffer prefix = allocator.Allocate(Math.Max(Eac3FrameMetadata.MaximumPrefixBytes, DtsFrameHeader.MaximumHeaderBytes));
                for (int stream = 0; stream < tracks.Count; stream++)
                {
                    MatroskaCodecConfiguration configuration = configurations[stream];
                    string codec = configuration.Stream.CodecId;
                    if (!RequiresTiming(configuration)) { continue; }
                    if (configuration.FrameDuration is MediaDuration period)
                    {
                        result.IndexFixedStream(stream, index, period, token);
                        continue;
                    }
                    if (codec == DtsFrameHeader.CodecId)
                    {
                        await result.ReadDtsStreamAsync(stream, source, index, blocks, tracks[stream].Audio!.Value, prefix.Memory, token).ConfigureAwait(false);
                        continue;
                    }
                    long count = index.GetPacketCount(stream);
                    if (count == 0) { continue; }
                    result.AllocateEntries(stream, index, count);
                    MatroskaAc3Programme programme = new(codec, tracks[stream].Audio!.Value);
                    int cachedBlock = -1, preceding = 0, groupOffset = 0, blockRate = 0;
                    long lastIndependent = -1;
                    for (long packet = 0; packet < count; packet++)
                    {
                        token.ThrowIfCancellationRequested();
                        MatroskaPacketAddress address = index.Locate(stream, packet);
                        if (cachedBlock != address.BlockIndex)
                        {
                            await blocks.ReadAsync(index.GetBlock(address.BlockIndex).Element, token).ConfigureAwait(false);
                            cachedBlock = address.BlockIndex;
                            preceding = 0; groupOffset = 0; blockRate = 0; lastIndependent = -1;
                        }
                        MatroskaFrameRange frame = blocks.GetFrame(address.FrameIndex);
                        if (frame.Length < Ac3FrameHeader.HeaderBytes)
                        { throw new InvalidDataException("The Matroska AC-3 syncframe header is truncated."); }
                        await source.ReadExactlyAtAsync(frame.Offset, prefix.Memory[..Ac3FrameHeader.HeaderBytes], token).ConfigureAwait(false);
                        Ac3FrameHeader header = Ac3FrameHeader.Read(prefix.Memory.Span);
                        if (header.IsEnhanced != (codec == Ac3FrameHeader.EnhancedCodecId) || header.FrameBytes != frame.Length)
                        { throw new InvalidDataException("A Matroska AC-3 frame must contain one complete syncframe of its declared codec."); }
                        if (programme.Observe(header))
                        {
                            int bytes = Math.Min(frame.Length, Eac3FrameMetadata.MaximumPrefixBytes);
                            await source.ReadExactlyAtAsync(frame.Offset + Ac3FrameHeader.HeaderBytes,
                                prefix.Memory.Slice(Ac3FrameHeader.HeaderBytes, bytes - Ac3FrameHeader.HeaderBytes), token).ConfigureAwait(false);
                            programme.AddOpening(Eac3FrameMetadata.Read(prefix.Memory.Span[..bytes]));
                        }
                        if (blockRate != 0 && blockRate != header.SampleRate)
                        { throw new NotSupportedException("Changes to the Matroska audio sample rate within a lace are not supported."); }
                        bool dependent = header.IsEnhanced && header.StreamType == 1;
                        if (!dependent)
                        {
                            for (long previous = lastIndependent; previous >= 0 && previous < packet; previous++)
                            { result.Entries(stream)[(int)previous] = result.Entries(stream)[(int)previous] with { IsFinalFrameSetInBlock = false }; }
                            groupOffset = preceding; lastIndependent = packet;
                            preceding = checked(preceding + header.SamplesPerFrame);
                        }
                        else
                        {
                            if (lastIndependent < 0) { lastIndependent = packet; }
                            if (preceding == 0) { preceding = header.SamplesPerFrame; }
                        }
                        result.Entries(stream)[(int)packet] = new(header.SampleRate, header.SamplesPerFrame,
                            groupOffset, header.FrameBytes, MatroskaAudioFrameTiming.GetIdentity(header), true);
                        blockRate = header.SampleRate;
                    }
                    programme.Complete(); result.metadata[stream] = programme.Metadata;
                }
                success = true;
                return result;
            }
            finally
            {
                if (!success) { result.Dispose(); }
            }
        }

        public MatroskaAudioFrameTiming? Get(int stream, long packet)
        {
            if (storage[stream] is null) { return null; }
            return Entries(stream)[checked((int)packet)];
        }

        public AudioStreamMetadata? GetMetadata(int stream) => metadata[stream];

        public bool HasStream(int stream) => storage[stream] is not null;

        public void SetPresentationTime(int stream, long packet, MediaTimestamp timestamp)
        {
            ref MatroskaAudioFrameTiming timing = ref Entries(stream)[checked((int)packet)];
            timing = timing with { PresentationTime = timestamp };
        }

        private static bool RequiresTiming(MatroskaCodecConfiguration configuration) =>
            configuration.Stream.Format == StreamFormats.Ac3 || configuration.Stream.Format == StreamFormats.Eac3Syncframe ||
            configuration.Stream.Format == StreamFormats.Dts ||
            (configuration.Stream.Kind == MediaKind.Audio && configuration.FrameDuration is not null);

        private void IndexFixedStream(int stream, MatroskaPacketIndex index, MediaDuration period, CancellationToken token)
        {
            long count = index.GetPacketCount(stream);
            if (count == 0)
            {
                return;
            }
            AllocateEntries(stream, index, count);
            int samples = checked((int)(period.Value * period.TimeBase.Numerator));
            Span<MatroskaAudioFrameTiming> entries = Entries(stream);
            for (int packet = 0; packet < entries.Length; packet++)
            {
                token.ThrowIfCancellationRequested();
                MatroskaPacketAddress address = index.Locate(stream, packet);
                bool lastInBlock = address.FrameIndex == index.GetBlock(address.BlockIndex).FrameCount - 1;
                entries[packet] = new(period.TimeBase.Denominator, samples, checked(samples * address.FrameIndex), 0, 0, lastInBlock);
            }
        }

        private void AllocateEntries(int stream, MatroskaPacketIndex index, long count)
        {
            int entryBytes = Unsafe.SizeOf<MatroskaAudioFrameTiming>();
            if (count > int.MaxValue / entryBytes)
            {
                throw new NotSupportedException("The Matroska audio timing index exceeds the maximum managed workspace size.");
            }
            storage[stream] = index.AllocatePacketMetadataStorage(checked((int)count * entryBytes));
        }

        public void ValidatePacket(int stream, long packet, ReadOnlySpan<byte> data, MatroskaTrack track)
        {
            MatroskaAudioFrameTiming timing = Entries(stream)[checked((int)packet)];
            if (track.CodecId == "A_DTS")
            {
                ValidateDtsPacket(data, timing);
                return;
            }
            Ac3FrameHeader header = Ac3FrameHeader.Read(data);
            if (header.IsEnhanced != (track.CodecId == "A_EAC3") || header.FrameBytes != data.Length ||
                header.FrameBytes != timing.FrameBytes || header.SamplesPerFrame != timing.Samples ||
                header.SampleRate != timing.SampleRate || MatroskaAudioFrameTiming.GetIdentity(header) != timing.HeaderIdentity)
            { throw new InvalidDataException("The Matroska AC-3 syncframe no longer matches its index."); }
        }

        private Span<MatroskaAudioFrameTiming> Entries(int stream) =>
            MemoryMarshal.Cast<byte, MatroskaAudioFrameTiming>(storage[stream]!.Memory.Span);

        public void Dispose()
        {
            foreach (WritableMediaBuffer? buffer in storage) { buffer?.Dispose(); }
        }
    }
}

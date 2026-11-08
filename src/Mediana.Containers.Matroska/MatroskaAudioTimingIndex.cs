// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Mediana.Core.Memory;
using Mediana.Core;
using Mediana.Formats;
using Mediana.Formats.Audio;
using Mediana.IO;

namespace Mediana.Containers.Matroska
{
    /// <summary>Exact self-describing audio clocks, obtained from bounded frame prefixes without decoding payloads.</summary>
    internal class MatroskaAudioTimingIndex : IDisposable
    {
        private readonly WritableMediaBuffer?[] storage;

        private MatroskaAudioTimingIndex(int tracks) => storage = new WritableMediaBuffer?[tracks];

        public static async ValueTask<MatroskaAudioTimingIndex?> CreateAsync(IRandomAccessByteSource source,
            MediaBufferAllocator allocator, MatroskaPacketIndex index, IReadOnlyList<MatroskaTrack> tracks,
            IReadOnlyList<MatroskaCodecConfiguration> configurations, MatroskaIndexOptions options, CancellationToken token)
        {
            bool required = false;
            foreach (MatroskaCodecConfiguration configuration in configurations)
            {
                if (RequiresTiming(configuration.Stream))
                { required = true; break; }
            }
            if (!required) { return null; }
            MatroskaAudioTimingIndex result = new(tracks.Count);
            bool success = false;
            try
            {
                using MatroskaBlockReader blocks = new(source, allocator, options.MaximumBlockHeaderBytes, options.MaximumFrameBytes);
                using WritableMediaBuffer prefix = allocator.Allocate(Ac3FrameHeader.HeaderBytes);
                for (int stream = 0; stream < tracks.Count; stream++)
                {
                    string codec = configurations[stream].Stream.CodecId;
                    if (!RequiresTiming(configurations[stream].Stream)) { continue; }
                    long count = index.GetPacketCount(stream);
                    if (count == 0) { continue; }
                    int entryBytes = Unsafe.SizeOf<MatroskaAudioFrameTiming>();
                    if (count > int.MaxValue / entryBytes)
                    { throw new NotSupportedException("The Matroska audio timing index exceeds the maximum managed workspace size."); }
                    result.storage[stream] = index.AllocatePacketMetadataStorage(checked((int)count * entryBytes));
                    int cachedBlock = -1, preceding = 0, blockRate = 0;
                    for (long packet = 0; packet < count; packet++)
                    {
                        token.ThrowIfCancellationRequested();
                        MatroskaPacketAddress address = index.Locate(stream, packet);
                        if (cachedBlock != address.BlockIndex)
                        {
                            await blocks.ReadAsync(index.GetBlock(address.BlockIndex).Element, token).ConfigureAwait(false);
                            cachedBlock = address.BlockIndex;
                            preceding = 0; blockRate = 0;
                        }
                        MatroskaFrameRange frame = blocks.GetFrame(address.FrameIndex);
                        if (frame.Length < Ac3FrameHeader.HeaderBytes)
                        { throw new InvalidDataException("The Matroska AC-3 syncframe header is truncated."); }
                        await source.ReadExactlyAtAsync(frame.Offset, prefix.Memory, token).ConfigureAwait(false);
                        Ac3FrameHeader header = Ac3FrameHeader.Read(prefix.Memory.Span);
                        if (header.IsEnhanced != (codec == Ac3FrameHeader.EnhancedCodecId) || header.FrameBytes != frame.Length)
                        { throw new InvalidDataException("A Matroska AC-3 frame must contain one complete syncframe of its declared codec."); }
                        MatroskaAudioSettings audio = tracks[stream].Audio!.Value;
                        if (audio.SamplingFrequency != header.SampleRate || audio.Channels != (ulong)header.ChannelCount)
                        { throw new InvalidDataException("The Matroska audio sampling format disagrees with its AC-3 syncframe."); }
                        if (blockRate != 0 && blockRate != header.SampleRate)
                        { throw new NotSupportedException("Changes to the Matroska audio sample rate within a lace are not supported."); }
                        result.Entries(stream)[(int)packet] = new(header.SampleRate, header.SamplesPerFrame, preceding, header.FrameBytes);
                        preceding = checked(preceding + header.SamplesPerFrame); blockRate = header.SampleRate;
                    }
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

        public bool HasStream(int stream) => storage[stream] is not null;

        private static bool RequiresTiming(EncodedStreamInfo stream) =>
            stream.Format == StreamFormats.Ac3 || stream.Format == StreamFormats.Eac3;

        public void ValidatePacket(int stream, long packet, ReadOnlySpan<byte> data, MatroskaTrack track)
        {
            MatroskaAudioFrameTiming timing = Entries(stream)[checked((int)packet)];
            Ac3FrameHeader header = Ac3FrameHeader.Read(data);
            if (header.IsEnhanced != (track.CodecId == "A_EAC3") || header.FrameBytes != data.Length ||
                header.FrameBytes != timing.FrameBytes || header.SamplesPerFrame != timing.Samples ||
                header.SampleRate != timing.SampleRate || header.ChannelCount != (long)track.Audio!.Value.Channels)
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

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Timing;
using Mediana.Formats.Audio;
using Mediana.IO;

namespace Mediana.Containers.Matroska
{
    internal partial class MatroskaAudioTimingIndex
    {
        private static readonly TimeBase opusClock = new(1, OpusConfiguration.ClockRate);

        private async ValueTask ReadOpusStreamAsync(int stream, IRandomAccessByteSource source, MatroskaPacketIndex index,
            MatroskaBlockReader blocks, MatroskaTrack track, EncodedStreamInfo streamInfo, Memory<byte> prefix, CancellationToken token)
        {
            OpusConfiguration opus = OpusConfiguration.Read(streamInfo.Configuration.Span);
            if (track.Audio!.Value.Channels != (ulong)opus.ChannelCount)
            {
                throw new InvalidDataException("The Matroska Opus channel count disagrees with OpusHead.");
            }
            ulong delay = track.Timing.CodecDelayNanoseconds;
            if (delay > long.MaxValue || ToOpusSamples((long)delay) != opus.PreSkipSamples)
            {
                throw new InvalidDataException("The Matroska Opus CodecDelay disagrees with OpusHead pre-skip.");
            }
            metadata[stream] = new(OpusConfiguration.ClockRate, opus.ChannelCount);
            long count = index.GetPacketCount(stream);
            if (count == 0)
            {
                return;
            }
            AllocateEntries(stream, index, count);
            int previousBlock = -1;
            int preceding = 0;
            for (int packet = 0; packet < count; packet++)
            {
                token.ThrowIfCancellationRequested();
                MatroskaPacketAddress address = index.Locate(stream, packet);
                MatroskaIndexedBlock block = index.GetBlock(address.BlockIndex);
                if (address.BlockIndex != previousBlock)
                {
                    await blocks.ReadAsync(block.Element, token).ConfigureAwait(false);
                    previousBlock = address.BlockIndex;
                    preceding = 0;
                }
                MatroskaFrameRange frame = blocks.GetFrame(address.FrameIndex);
                Memory<byte> toc = prefix[..Math.Min(frame.Length, 2)];
                await source.ReadExactlyAtAsync(frame.Offset, toc, token).ConfigureAwait(false);
                int samples = OpusPacketInfo.ReadTiming(toc.Span).SampleCount;
                bool last = address.FrameIndex == block.FrameCount - 1;
                Entries(stream)[packet] = new(OpusConfiguration.ClockRate, samples, preceding, frame.Length, 0, last);
                preceding = checked(preceding + samples);
                if (last && index.GetExtensions(address.BlockIndex)?.DiscardPaddingNanoseconds is > 0 and long padding)
                {
                    TrimOpusBlockEnd(Entries(stream), packet - address.FrameIndex, packet, preceding, padding);
                }
            }
        }

        private static void TrimOpusBlockEnd(Span<MatroskaAudioFrameTiming> entries, int first, int last,
            int blockSamples, long padding)
        {
            long padSamples = ToOpusSamples(padding);
            if (padSamples > blockSamples)
            {
                throw new InvalidDataException("Matroska Opus discard padding exceeds its block duration.");
            }
            int valid = blockSamples - (int)padSamples;
            for (int packet = last; packet >= first; packet--)
            {
                MatroskaAudioFrameTiming timing = entries[packet];
                int samples = Math.Clamp(valid - timing.PrecedingSamples, 0, timing.Samples);
                if (samples == timing.Samples)
                {
                    break;
                }
                entries[packet] = timing with { PresentedSamples = samples };
            }
        }

        private static long ToOpusSamples(long nanoseconds) =>
            new MediaTimestamp(nanoseconds, MatroskaTrackClock.TimeBase).Rescale(opusClock, TimestampRounding.NearestEven).Value;
    }
}

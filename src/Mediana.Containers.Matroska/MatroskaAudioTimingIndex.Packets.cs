// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.IO;

namespace Mediana.Containers.Matroska
{
    internal partial class MatroskaAudioTimingIndex
    {
        private async ValueTask ReadPacketTimedStreamAsync(int stream, IRandomAccessByteSource source, MatroskaPacketIndex index,
            MatroskaBlockReader blocks, MatroskaTrack track, EncodedStreamInfo streamInfo, Memory<byte> prefix, CancellationToken token)
        {
            MatroskaAudioPacketSamples clock = new(streamInfo, track, token);
            metadata[stream] = clock.Metadata;
            int rate = clock.Metadata.SampleRate;
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
                Memory<byte> packetHeader = prefix[..Math.Min(frame.Length, clock.PrefixBytes)];
                await source.ReadExactlyAtAsync(frame.Offset, packetHeader, token).ConfigureAwait(false);
                int samples = clock.Read(packetHeader.Span);
                bool last = address.FrameIndex == block.FrameCount - 1;
                Entries(stream)[packet] = new(rate, samples, preceding, frame.Length, 0, last);
                preceding = checked(preceding + samples);
                if (last && MatroskaAudioPacketSamples.SupportsEndTrimming(streamInfo.Format) &&
                    index.GetExtensions(address.BlockIndex)?.DiscardPaddingNanoseconds is > 0 and long padding)
                {
                    long paddingSamples = MatroskaAudioPacketSamples.ToSamples(padding, rate);
                    TrimBlockEnd(Entries(stream), packet - address.FrameIndex, packet, preceding, paddingSamples);
                }
            }
        }

        private static void TrimBlockEnd(Span<MatroskaAudioFrameTiming> entries, int first, int last,
            int blockSamples, long padSamples)
        {
            if (padSamples > blockSamples)
            {
                throw new InvalidDataException("Matroska audio discard padding exceeds its block duration.");
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
    }
}

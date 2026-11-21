// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using Mediana.Core.Metadata;
using Mediana.Formats.Audio;
using Mediana.IO;

namespace Mediana.Containers.Matroska
{
    internal partial class MatroskaAudioTimingIndex
    {
        private async ValueTask ReadDtsStreamAsync(int stream, IRandomAccessByteSource source,
            MatroskaPacketIndex index, MatroskaBlockReader blocks, MatroskaAudioSettings settings,
            Memory<byte> prefix, CancellationToken token)
        {
            long count = index.GetPacketCount(stream);
            if (count == 0) { return; }
            int entryBytes = Unsafe.SizeOf<MatroskaAudioFrameTiming>();
            if (count > int.MaxValue / entryBytes)
            { throw new NotSupportedException("The Matroska audio timing index exceeds the maximum managed workspace size."); }
            storage[stream] = index.AllocatePacketMetadataStorage(checked((int)count * entryBytes));
            int cachedBlock = -1, preceding = 0, rate = 0;
            for (long packet = 0; packet < count; packet++)
            {
                token.ThrowIfCancellationRequested();
                MatroskaPacketAddress address = index.Locate(stream, packet);
                if (cachedBlock != address.BlockIndex)
                {
                    await blocks.ReadAsync(index.GetBlock(address.BlockIndex).Element, token).ConfigureAwait(false);
                    cachedBlock = address.BlockIndex;
                    preceding = 0;
                    rate = 0;
                }
                MatroskaFrameRange frame = blocks.GetFrame(address.FrameIndex);
                if (frame.Length < DtsFrameHeader.MaximumHeaderBytes)
                { throw new InvalidDataException("The Matroska DTS header is truncated."); }
                await source.ReadExactlyAtAsync(frame.Offset, prefix[..DtsFrameHeader.MaximumHeaderBytes], token).ConfigureAwait(false);
                DtsFrameHeader header = DtsFrameHeader.Read(prefix.Span[..DtsFrameHeader.MaximumHeaderBytes]);
                if (header.FrameBytes > frame.Length)
                { throw new InvalidDataException("The DTS core frame exceeds its Matroska block."); }
                if (!header.HasExtensions && header.FrameBytes == frame.Length &&
                    (settings.SamplingFrequency != header.SampleRate || settings.OutputSamplingFrequency != header.SampleRate ||
                     settings.Channels != (ulong)header.ChannelCount))
                { throw new InvalidDataException("The DTS core format differs from its Matroska track."); }
                if (rate != 0 && rate != header.SampleRate)
                { throw new NotSupportedException("The DTS sample rate changes within a Matroska lace."); }
                if (packet == 0)
                {
                    string[] channels = header.GetChannelOrder();
                    metadata[stream] = new(header.SampleRate, channels.Length, channelOrder: channels)
                    { CodecProfile = new("DTS Core") };
                }
                Entries(stream)[(int)packet] = new(header.SampleRate, header.SamplesPerFrame, preceding,
                    frame.Length, MatroskaAudioFrameTiming.GetIdentity(header), address.FrameIndex == index.GetBlock(address.BlockIndex).FrameCount - 1);
                preceding = checked(preceding + header.SamplesPerFrame);
                rate = header.SampleRate;
            }
        }

        private static void ValidateDtsPacket(ReadOnlySpan<byte> data, MatroskaAudioFrameTiming timing)
        {
            DtsFrameHeader header = DtsFrameHeader.Read(data);
            if (header.FrameBytes > data.Length || data.Length != timing.FrameBytes || header.SampleRate != timing.SampleRate ||
                header.SamplesPerFrame != timing.Samples || MatroskaAudioFrameTiming.GetIdentity(header) != timing.HeaderIdentity)
            { throw new InvalidDataException("The Matroska DTS frame no longer matches its timing index."); }
        }
    }
}

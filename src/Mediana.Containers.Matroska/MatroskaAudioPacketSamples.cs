// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Metadata;
using Mediana.Core.Timing;
using Mediana.Formats;
using Mediana.Formats.Audio;

namespace Mediana.Containers.Matroska
{
    internal class MatroskaAudioPacketSamples
    {
        private readonly string codecId;
        private readonly FlacConfiguration? flac;
        private readonly VorbisSetupInfo? vorbis;
        private int previousBlockSize;

        public MatroskaAudioPacketSamples(EncodedStreamInfo stream, MatroskaTrack track, CancellationToken token)
        {
            codecId = stream.CodecId;
            switch (codecId)
            {
                case OpusConfiguration.CodecId:
                    OpusConfiguration opus = OpusConfiguration.Read(stream.Configuration.Span);
                    ulong delay = track.Timing.CodecDelayNanoseconds;
                    if (delay > long.MaxValue ||
                        ToSamples((long)delay, OpusConfiguration.ClockRate) != opus.PreSkipSamples)
                    {
                        throw new InvalidDataException("The Matroska Opus CodecDelay disagrees with OpusHead pre-skip.");
                    }
                    Metadata = new(OpusConfiguration.ClockRate, opus.ChannelCount);
                    break;
                case FlacConfiguration.CodecId:
                    flac = FlacConfiguration.ReadStreamInfo(stream.Configuration.Span);
                    Metadata = new(flac.SampleRate, flac.ChannelCount, flac.BitsPerSample);
                    break;
                case VorbisIdentification.CodecId:
                    VorbisConfiguration configuration = VorbisConfiguration.Read(stream.Configuration);
                    VorbisIdentification identification = configuration.Identification;
                    vorbis = VorbisSetupInfo.Read(identification, configuration.SetupHeader.Span, token);
                    Metadata = new(identification.SampleRate, identification.ChannelCount,
                        channelOrder: VorbisChannelOrder.Create(identification.ChannelCount));
                    break;
                default:
                    throw new InvalidOperationException("The Matroska audio packet timing format is unsupported.");
            }
            if (track.Audio!.Value.Channels != (ulong)Metadata.ChannelCount)
            {
                throw new InvalidDataException("The Matroska audio channel count disagrees with its codec configuration.");
            }
        }

        public AudioStreamMetadata Metadata { get; }
        public int PrefixBytes => flac is null ? 2 : FlacFrameHeader.MaximumHeaderBytes;

        public static bool HasNativeSampleCounts(EncodedStreamFormat format) =>
            format == StreamFormats.Opus || format == StreamFormats.Vorbis || format == StreamFormats.FlacFrame;

        public static bool SupportsEndTrimming(EncodedStreamFormat format) =>
            format == StreamFormats.Opus || format == StreamFormats.Vorbis;

        public int Read(ReadOnlySpan<byte> prefix)
        {
            switch (codecId)
            {
                case OpusConfiguration.CodecId:
                    return OpusPacketInfo.ReadTiming(prefix).SampleCount;
                case FlacConfiguration.CodecId:
                    return FlacFrameHeader.TryRead(prefix, flac!, out FlacFrameHeader header)
                        ? header.BlockSize : throw new InvalidDataException("The Matroska FLAC frame header is truncated.");
                case VorbisIdentification.CodecId:
                    int blockSize = vorbis!.GetBlockSize(prefix);
                    int samples = previousBlockSize == 0 ? 0 : (previousBlockSize + blockSize) / 4;
                    previousBlockSize = blockSize;
                    return samples;
                default:
                    throw new InvalidOperationException("The Matroska audio packet timing format is unsupported.");
            }
        }

        public static long ToSamples(long nanoseconds, int rate) =>
            new MediaTimestamp(nanoseconds, MatroskaTrackClock.TimeBase).Rescale(new(1, rate), TimestampRounding.NearestEven).Value;
    }
}

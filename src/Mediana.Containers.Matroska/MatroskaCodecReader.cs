// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Formats;
using Mediana.Formats.Audio;
using Mediana.IO;

namespace Mediana.Containers.Matroska
{
    internal static class MatroskaCodecReader
    {
        public static async ValueTask<IReadOnlyList<MatroskaCodecConfiguration>> ReadAsync(IRandomAccessByteSource source,
            MediaBufferAllocator allocator, IReadOnlyList<MatroskaTrack> tracks, MatroskaPacketIndex index,
            MatroskaIndexOptions options, int maximumConfigurationBytes, int maximumTotalConfigurationBytes,
            CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(allocator);
            ArgumentNullException.ThrowIfNull(tracks);
            ArgumentNullException.ThrowIfNull(index);
            options.Validate();
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumConfigurationBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumTotalConfigurationBytes);
            if (tracks.Count != index.TrackCount)
            {
                throw new ArgumentException("The Matroska tracks and packet index must have the same declaration order.", nameof(tracks));
            }
            MatroskaCodecConfiguration[] configurations = new MatroskaCodecConfiguration[tracks.Count];
            int totalBytes = 0;
            for (int streamIndex = 0; streamIndex < tracks.Count; streamIndex++)
            {
                token.ThrowIfCancellationRequested();
                MatroskaTrack track = tracks[streamIndex];
                if (track.ContentEncodings is not null || track.TrackOperation is not null)
                {
                    throw new NotSupportedException("Matroska content encodings and composed tracks are not supported.");
                }
                (MediaKind kind, string codecId, EncodedStreamFormat format) = (track.Type, track.CodecId) switch
                {
                    (1, "V_MPEG4/ISO/AVC") when track.Video is not null => (MediaKind.Video, "h264", StreamFormats.Avc),
                    (2, "A_AAC") when track.Audio is not null => (MediaKind.Audio, AacConfiguration.CodecId, StreamFormats.Aac),
                    (2, "A_MPEG/L3") when track.Audio is not null => (MediaKind.Audio, Mp3FrameHeader.CodecId, StreamFormats.Mp3),
                    (1, "V_MPEG4/ISO/AVC") or (2, "A_AAC" or "A_MPEG/L3") =>
                        throw new InvalidDataException("The Matroska codec mapping requires its Video or Audio settings."),
                    _ => throw new NotSupportedException($"The Matroska track mapping '{track.CodecId}' with type {track.Type} is not supported.")
                };
                int configurationBytes = codecId == Mp3FrameHeader.CodecId ? Mp3FrameHeader.HeaderBytes :
                    GetConfigurationSize(track.CodecPrivate, source.Length);
                if (configurationBytes > maximumConfigurationBytes || configurationBytes > maximumTotalConfigurationBytes - totalBytes)
                {
                    throw new NotSupportedException("Matroska codec configurations exceed the configured byte limits.");
                }
                using WritableMediaBuffer bytes = allocator.Allocate(configurationBytes);
                Mp3FrameHeader? mp3 = null;
                if (codecId == Mp3FrameHeader.CodecId)
                {
                    mp3 = await ReadMpegHeaderAsync(source, allocator, track, streamIndex, index, options, bytes.Memory, token).ConfigureAwait(false);
                }
                else
                {
                    await source.ReadExactlyAtAsync(track.CodecPrivate!.Value.DataOffset, bytes.Memory, token).ConfigureAwait(false);
                }
                token.ThrowIfCancellationRequested();
                MediaDuration? duration = null;
                switch (codecId)
                {
                    case "h264":
                        if (bytes.Memory.Length < 7 || bytes.Memory.Span[0] != 1)
                        {
                            throw new InvalidDataException("Matroska AVC requires an AVCDecoderConfigurationRecord.");
                        }
                        break;
                    case AacConfiguration.CodecId:
                        AacConfiguration? aac = AacConfiguration.ReadAudioSpecificConfig(bytes.Memory.Span, out _);
                        if (aac is not null)
                        {
                            duration = new(aac.SamplesPerFrame, new TimeBase(1, aac.SampleRate));
                        }
                        break;
                    case Mp3FrameHeader.CodecId:
                        duration = new(mp3!.SamplesPerFrame, new TimeBase(1, mp3.SampleRate));
                        break;
                    default:
                        throw new InvalidOperationException("The Matroska codec mapping is inconsistent.");
                }
                configurations[streamIndex] = new(new(streamIndex, kind, codecId, format, MatroskaTrackClock.TimeBase, bytes.Memory.Span), duration, mp3);
                totalBytes += configurationBytes;
            }
            token.ThrowIfCancellationRequested();
            return Array.AsReadOnly(configurations);
        }

        private static int GetConfigurationSize(EbmlElement? element, long? sourceLength)
        {
            if (element is not EbmlElement value || value.DataOffset < 0 || value.EndOffset is not long end || end <= value.DataOffset ||
                (sourceLength is long length && end > length))
            {
                throw new InvalidDataException("The Matroska codec configuration is missing, empty or truncated.");
            }
            if (end - value.DataOffset > int.MaxValue)
            {
                throw new NotSupportedException("The Matroska codec configuration exceeds the supported byte range.");
            }
            return (int)(end - value.DataOffset);
        }

        private static async ValueTask<Mp3FrameHeader> ReadMpegHeaderAsync(IRandomAccessByteSource source,
            MediaBufferAllocator allocator, MatroskaTrack track, int streamIndex, MatroskaPacketIndex index,
            MatroskaIndexOptions options, Memory<byte> destination, CancellationToken token)
        {
            if (index.GetPacketCount(streamIndex) == 0)
            {
                throw new NotSupportedException("Matroska MP3 configuration requires a first frame.");
            }
            MatroskaPacketAddress address = index.Locate(streamIndex, 0);
            using MatroskaBlockReader reader = new(source, allocator, options.MaximumBlockHeaderBytes, options.MaximumFrameBytes);
            MatroskaBlockHeader block = await reader.ReadAsync(index.GetBlock(address.BlockIndex).Element, token).ConfigureAwait(false);
            if (block.TrackNumber != track.Number)
            {
                throw new InvalidDataException("The Matroska MP3 block no longer matches its track.");
            }
            MatroskaFrameRange frame = reader.GetFrame(address.FrameIndex);
            if (frame.Length < Mp3FrameHeader.HeaderBytes)
            {
                throw new InvalidDataException("The first Matroska MP3 frame header is truncated.");
            }
            await source.ReadExactlyAtAsync(frame.Offset, destination, token).ConfigureAwait(false);
            Mp3FrameHeader header = Mp3FrameHeader.Read(destination.Span);
            if (header.FrameBytes != frame.Length)
            {
                throw new InvalidDataException("A Matroska MP3 frame must contain exactly one complete Layer III frame.");
            }
            return header;
        }
    }
}

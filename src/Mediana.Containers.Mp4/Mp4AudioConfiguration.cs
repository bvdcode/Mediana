// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Containers.Mp4
{
    internal static class Mp4AudioConfiguration
    {
        public static async ValueTask ReadAsync(Mp4Movie movie, IRandomAccessByteSource source,
            MediaBufferAllocator allocator, Mp4ReadOptions options, CancellationToken cancellationToken)
        {
            for (int index = 0; index < movie.Tracks.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Mp4TrackInfo track = movie.Tracks[index];
                if (track.Stream.CodecId != Mp3FrameHeader.CodecId)
                {
                    continue;
                }
                if (track.SampleCount == 0 || options.MaxCodecConfigurationBytes < Mp3FrameHeader.HeaderBytes)
                {
                    throw new NotSupportedException("MP4 MP3 configuration requires a first frame and a four-byte configuration budget.");
                }
                Mp4SampleLocation first = new Mp4TrackCursor(movie.Tables[index]).Current;
                if (first.Size < Mp3FrameHeader.HeaderBytes)
                {
                    throw new InvalidDataException("The first MP4 MPEG audio sample has a truncated frame header.");
                }
                using WritableMediaBuffer bytes = allocator.Allocate(Mp3FrameHeader.HeaderBytes);
                await source.ReadExactlyAtAsync(first.Offset, bytes.Memory, cancellationToken).ConfigureAwait(false);
                Mp3FrameHeader header = Mp3FrameHeader.Read(bytes.Memory.Span);
                if (header.FrameBytes != first.Size)
                {
                    throw new NotSupportedException("MP4 MP3 samples must contain one complete Layer III frame.");
                }
                EncodedStreamInfo stream = new(track.Stream.StreamIndex, MediaKind.Audio, Mp3FrameHeader.CodecId,
                    track.Stream.TimeBase, bytes.Memory.Span);
                movie.Tracks[index] = track.WithStream(stream);
            }
        }
    }
}

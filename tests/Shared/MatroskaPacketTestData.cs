// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Matroska;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Formats;
using Mediana.Formats.Audio;
using static Mediana.Tests.MatroskaMetadataTestData;
using static Mediana.Tests.MatroskaIndexTestData;

namespace Mediana.Tests
{
    internal static class MatroskaPacketTestData
    {
        public static MatroskaTrack AudioTrack(byte[] configuration, ulong number = 1) => Track(number) with
        {
            Audio = new(48000, 48000, 1, null),
            CodecPrivate = new(0x63A2, Data(configuration).Length - configuration.Length, Data(configuration).Length)
        };

        public static MatroskaCodecConfiguration AacStream(int stream = 0, int rate = 48000) => new(
            new(stream, MediaKind.Audio, "aac", StreamFormats.Aac, MatroskaTrackClock.TimeBase,
                AacConfiguration.Create(rate, 1).ToAudioSpecificConfig()), new(1024, new TimeBase(1, rate)));

        public static byte[] Data(byte[] configuration, params byte[][] fields) =>
            [.. Field(0x1654AE6B, Field(0xAE, [.. TrackFields(), .. Field(0xE1, []), .. Field(0x63A2, configuration)])),
                .. fields.SelectMany(value => value)];

        public static ValueTask<IReadOnlyList<MatroskaCodecConfiguration>> ReadCodecsAsync(EbmlElementTestSource source,
            MediaBufferAllocator allocator, IReadOnlyList<MatroskaTrack> tracks, MatroskaPacketIndex index,
            int maximumBytes = 65536, int maximumTotalBytes = 1048576, CancellationToken token = default)
            => MatroskaCodecReader.ReadAsync(source, allocator, tracks, index, Options, maximumBytes, maximumTotalBytes, token);

        public static async Task<(MatroskaHeader Header, EbmlElement Segment, MatroskaSegmentInfo Info, IReadOnlyList<MatroskaTrack> Tracks)>
            ReadMetadataAsync(EbmlElementTestSource source, MediaBufferAllocator allocator)
        {
            MatroskaHeader header = await MatroskaHeaderReader.ReadAsync(source, allocator, 256);
            using MatroskaSegmentReader segments = new(source, allocator, header.MaximumSizeWidth);
            using MatroskaMetadataReader metadata = new(source, allocator, Limits, header.MaximumSizeWidth);
            EbmlElement segment = await segments.ReadRootAsync(header.BodyOffset);
            MatroskaSegmentInfo? info = null;
            IReadOnlyList<MatroskaTrack>? tracks = null;
            long offset = segment.DataOffset;
            while (await segments.ReadChildAsync(segment, offset) is EbmlElement child)
            {
                switch (child.Id)
                {
                    case 0x1654AE6B: tracks = await MatroskaTracksReader.ReadAsync(metadata, child); break;
                    case 0x1549A966: info = await MatroskaInfoReader.ReadAsync(metadata, child); break;
                }
                offset = child.EndOffset!.Value;
            }
            return (header, segment, info ?? throw new InvalidDataException("Missing Matroska Info."),
                tracks ?? throw new InvalidDataException("Missing Matroska Tracks."));
        }
    }
}

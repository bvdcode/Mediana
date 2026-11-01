// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Containers.Matroska;
using Mediana.Core.Memory;
using static Mediana.Tests.MatroskaMetadataTestData;

namespace Mediana.Tests
{
    internal static class MatroskaMetadataReferenceInput
    {
        public static async Task VerifyAsync()
        {
            foreach (string name in new[] { "avc-aac-attachment.mkv", "aac-lace-xiph.mka", "aac-lace-fixed.mka", "aac-lace-ebml.mka", "aac-lace-group-ebml.mka" })
            {
                await VerifyFileAsync(name);
            }
        }

        private static async Task VerifyFileAsync(string name)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Data", "Matroska", name);
            byte[] bytes = await File.ReadAllBytesAsync(path);
            using JsonDocument reference = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.ChangeExtension(path, ".metadata.json")));
            JsonElement expected = reference.RootElement;
            Require(Convert.ToHexStringLower(SHA256.HashData(bytes)) == expected.GetProperty("input_sha256").GetString());
            await using EbmlElementTestSource source = new(bytes) { MaximumRead = 3 };
            MediaBufferAllocator allocator = new(8192);
            MatroskaHeader header = await MatroskaHeaderReader.ReadAsync(source, allocator, 256);
            MatroskaSegmentInfo? info = null;
            IReadOnlyList<MatroskaTrack>? tracks = null;
            using (MatroskaSegmentReader segments = new(source, allocator, header.MaximumSizeWidth))
            using (MatroskaMetadataReader metadata = new(source, allocator, new(8, 8192, 512), header.MaximumSizeWidth))
            {
                EbmlElement segment = await segments.ReadRootAsync(header.BodyOffset);
                long offset = segment.DataOffset;
                while (await segments.ReadChildAsync(segment, offset) is EbmlElement child)
                {
                    switch (child.Id)
                    {
                        case 0x1549A966: info = await MatroskaInfoReader.ReadAsync(metadata, child); break;
                        case 0x1654AE6B: tracks = await MatroskaTracksReader.ReadAsync(metadata, child); break;
                    }
                    offset = child.EndOffset!.Value;
                }
                await segments.VerifyDocumentEndAsync(segment);
                Require(allocator.AllocatedBytes == 4120);
            }
            Require(allocator.AllocatedBytes == 0 && !source.IsDisposed);
            if (info is null || tracks is null)
            {
                throw new InvalidDataException("A reference file did not expose its Matroska Info and Tracks.");
            }
            CheckInfo(info, expected.GetProperty("info"));
            JsonElement nativeTracks = expected.GetProperty("tracks");
            JsonElement externalTracks = expected.GetProperty("external_streams");
            Require(tracks.Count == nativeTracks.GetArrayLength() && tracks.Count == externalTracks.GetArrayLength());
            for (int index = 0; index < tracks.Count; index++)
            {
                CheckTrack(tracks[index], nativeTracks[index], externalTracks[index], bytes);
            }
        }

        private static void CheckInfo(MatroskaSegmentInfo actual, JsonElement expected)
        {
            Require(actual.TimestampScale == expected.GetProperty("timestamp_scale").GetUInt64());
            Require(actual.Duration == expected.GetProperty("duration_ticks").GetDouble());
            Require(actual.Title == expected.GetProperty("title").GetString());
            Require(actual.MuxingApp == expected.GetProperty("muxing_app").GetString());
            Require(actual.WritingApp == expected.GetProperty("writing_app").GetString());
            string? uid = expected.GetProperty("uid").GetString();
            if (uid is null)
            {
                Require(actual.Uid is null);
            }
            else
            {
                Require(actual.Uid == Guid.ParseExact(uid, "N"));
            }
            Require(actual.DateNanoseconds is null && actual.PreviousSegment is null && actual.NextSegment is null);
        }

        private static void CheckTrack(MatroskaTrack actual, JsonElement expected, JsonElement external, byte[] source)
        {
            Require(actual.Number == expected.GetProperty("number").GetUInt64() && actual.Uid == expected.GetProperty("uid").GetUInt64());
            Require(actual.Type == expected.GetProperty("type").GetUInt64() && actual.CodecId == expected.GetProperty("codec_id").GetString());
            Require(actual.Flags == new MatroskaTrackFlags(expected.GetProperty("enabled").GetBoolean(), expected.GetProperty("default").GetBoolean(),
                expected.GetProperty("forced").GetBoolean(), expected.GetProperty("lacing").GetBoolean()));
            Require(actual.Timing == new MatroskaTrackTiming(expected.GetProperty("timestamp_scale").GetDouble(),
                NullableUnsigned(expected.GetProperty("default_duration_ns")), expected.GetProperty("codec_delay_ns").GetUInt64(),
                expected.GetProperty("seek_preroll_ns").GetUInt64()));
            Require(actual.Labels == new MatroskaTrackLabels(expected.GetProperty("name").GetString(),
                expected.GetProperty("language").GetString()!, expected.GetProperty("language_bcp47").GetString()));
            JsonElement configuration = expected.GetProperty("codec_private");
            EbmlElement privateData = actual.CodecPrivate ?? throw new InvalidDataException("A reference track has no CodecPrivate extent.");
            Require(privateData.DataOffset == configuration.GetProperty("offset").GetInt64() && privateData.DataSize == configuration.GetProperty("bytes").GetInt32());
            string hash = Convert.ToHexStringLower(SHA256.HashData(source.AsSpan((int)privateData.DataOffset, (int)privateData.DataSize!.Value)));
            Require(hash == configuration.GetProperty("sha256").GetString() && "SHA256:" + hash == external.GetProperty("extradata_hash").GetString());
            Require((actual.Video is not null) == (expected.GetProperty("video").ValueKind != JsonValueKind.Null));
            Require((actual.Audio is not null) == (expected.GetProperty("audio").ValueKind != JsonValueKind.Null));
            if (actual.Video is MatroskaVideoSettings video)
            {
                JsonElement native = expected.GetProperty("video");
                Require(video.PixelWidth == native.GetProperty("pixel_width").GetUInt64() && video.PixelHeight == native.GetProperty("pixel_height").GetUInt64());
                Require(video.PixelWidth == external.GetProperty("width").GetUInt64() && video.PixelHeight == external.GetProperty("height").GetUInt64());
                Require(actual.CodecId == "V_MPEG4/ISO/AVC" && external.GetProperty("codec_name").GetString() == "h264");
            }
            if (actual.Audio is MatroskaAudioSettings audio)
            {
                JsonElement native = expected.GetProperty("audio");
                Require(audio == new MatroskaAudioSettings(native.GetProperty("sampling_frequency").GetDouble(), native.GetProperty("output_sampling_frequency").GetDouble(),
                    native.GetProperty("channels").GetUInt64(), NullableUnsigned(native.GetProperty("bit_depth"))));
                Require(audio.SamplingFrequency.ToString(System.Globalization.CultureInfo.InvariantCulture) == external.GetProperty("sample_rate").GetString());
                Require(audio.Channels == external.GetProperty("channels").GetUInt64() && actual.CodecId == "A_AAC" && external.GetProperty("codec_name").GetString() == "aac");
            }
        }

        private static ulong? NullableUnsigned(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.Number => value.GetUInt64(),
            _ => throw new InvalidDataException("The native metadata reference has an invalid scalar type.")
        };
    }
}

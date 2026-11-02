// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text;
using Mediana.Containers.Matroska;
using static Mediana.Tests.MatroskaMetadataTestData;

namespace Mediana.Tests
{
    internal static class MatroskaMetadataTestInput
    {
        public static async Task VerifyInfoAsync()
        {
            MatroskaSegmentInfo defaults = await InfoAsync(InfoFields);
            Require(defaults == new MatroskaSegmentInfo(1000000, null, null, null, "a", "bb", null, null, null));
            Require(await InfoAsync([.. InfoFields, .. Field(0x2AD7B1, [])]) == defaults);
            byte[] uid = Convert.FromHexString("00112233445566778899aabbccddeeff");
            byte[] previous = Convert.FromHexString("10112233445566778899aabbccddeeff");
            byte[] next = Convert.FromHexString("20112233445566778899aabbccddeeff");
            byte[] body = [.. Field(0x73A4, uid), .. Field(0x3CB923, previous), .. Field(0x3EB923, next),
                .. Float(0x4489, 64.125), .. Unsigned(0x2AD7B1, ulong.MaxValue),
                .. Unsigned(0x4461, 0x8000000000000000), .. Field(0x7BA9, [.. Encoding.UTF8.GetBytes("Заголовок"), 0, 255]), .. InfoFields];
            MatroskaSegmentInfo info = await InfoAsync(body);
            Require(info.TimestampScale == ulong.MaxValue && info.Duration == 64.125 && info.DateNanoseconds == long.MinValue);
            Require(info.Title == "Заголовок" && info.Uid == Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"));
            Require(info.PreviousSegment == Guid.Parse("10112233-4455-6677-8899-aabbccddeeff") &&
                info.NextSegment == Guid.Parse("20112233-4455-6677-8899-aabbccddeeff"));
            Require((await InfoAsync([.. InfoFields, .. Field(0x4461, [])])).DateNanoseconds == 0);
            Require((await InfoAsync([.. InfoFields, .. Unsigned(0x4461, long.MaxValue)])).DateNanoseconds == long.MaxValue);
            Require((await InfoAsync([.. InfoFields, .. Field(0x80, []), .. Field(0x80, []), .. Field(0xEC, [])])) == defaults);
        }

        public static async Task VerifyTracksAsync()
        {
            MatroskaTrack defaults = (await TracksAsync(Field(0xAE, [.. TrackFields(), .. Field(0xE1, [])])))[0];
            Require(defaults.Number == 1 && defaults.Uid == 17 && defaults.Type == 2 && defaults.CodecId == "A_AAC");
            Require(defaults.Flags == new MatroskaTrackFlags(true, true, false, true));
            Require(defaults.Timing == new MatroskaTrackTiming(1, null, 0, 0));
            Require(defaults.Labels == new MatroskaTrackLabels(null, "eng", null));
            Require(defaults.Audio == new MatroskaAudioSettings(8000, 8000, 1, null) && defaults.Video is null);
            byte[] audio = [.. Field(0x78B5, []), .. Float(0xB5, 48000), .. Unsigned(0x9F, 2), .. Unsigned(0x6264, 16)];
            byte[] body = [.. TrackFields(7), .. Field(0x22B59C, []), .. Field(0x22B59D, "ru-RU"u8),
                .. Field(0x536E, Encoding.UTF8.GetBytes("Звук")), .. Field(0x63A2, [1, 2, 3]),
                .. Unsigned(0xB9, 0), .. Unsigned(0x88, 0), .. Unsigned(0x55AA, 1), .. Unsigned(0x9C, 0),
                .. Float(0x23314F, 0.5), .. Unsigned(0x23E383, ulong.MaxValue), .. Unsigned(0x56AA, 2417333),
                .. Unsigned(0x56BB, ulong.MaxValue), .. Field(0xE1, audio), .. Field(0x6D80, []), .. Field(0xE2, [])];
            byte[] payload = [.. Field(0xAE, body), .. Field(0xAE, TrackFields(2, 999))];
            IReadOnlyList<MatroskaTrack> tracks = await TracksAsync(payload);
            MatroskaTrack track = tracks[0];
            Require(tracks.Count == 2 && tracks[1].Number == 2 && tracks[1].Type == 999);
            Require(track.Flags == new MatroskaTrackFlags(false, false, true, false));
            Require(track.Timing == new MatroskaTrackTiming(0.5, ulong.MaxValue, 2417333, ulong.MaxValue));
            Require(track.Labels == new MatroskaTrackLabels("Звук", "eng", "ru-RU"));
            Require(track.Audio == new MatroskaAudioSettings(48000, 48000, 2, 16));
            Require(track.CodecPrivate is EbmlElement configuration && configuration.DataSize == 3 &&
                payload.AsSpan((int)configuration.DataOffset, 3).SequenceEqual(new byte[] { 1, 2, 3 }));
            Require(track.ContentEncodings?.Id == 0x6D80 && track.TrackOperation?.Id == 0xE2);
            Require(tracks is ICollection<MatroskaTrack> { IsReadOnly: true });
        }

        public static async Task VerifyVideoAsync()
        {
            byte[] dimensions = [.. Unsigned(0xB0, 1920), .. Unsigned(0xBA, 1080)];
            byte[] cropping = [.. Unsigned(0x54AA, 1), .. Unsigned(0x54BB, 3), .. Unsigned(0x54CC, 4), .. Unsigned(0x54DD, 16)];
            MatroskaVideoSettings video = await VideoAsync([.. Field(0x54B0, []), .. Field(0x54BA, []), .. dimensions, .. cropping]);
            Require(video.PixelWidth == 1920 && video.PixelHeight == 1080 && video.DisplayWidth == 1900 && video.DisplayHeight == 1076);
            Require(video.CropBottom == 1 && video.CropTop == 3 && video.CropLeft == 4 && video.CropRight == 16 &&
                video.DisplayUnit == 0 && video.Interlaced == 0 && video.FieldOrder == 2);
            MatroskaVideoSettings physical = await VideoAsync([.. dimensions, .. Unsigned(0x54B2, 3)]);
            Require(physical.DisplayWidth is null && physical.DisplayHeight is null);
            physical = await VideoAsync([.. Unsigned(0x54B0, 4), .. Unsigned(0x54BA, 3), .. dimensions, .. Unsigned(0x54B2, 3),
                .. Unsigned(0x9A, 1), .. Unsigned(0x9D, 14), .. Field(0x55B0, []), .. Field(0x7670, [])]);
            Require(physical.DisplayWidth == 4 && physical.DisplayHeight == 3 && physical.Interlaced == 1 && physical.FieldOrder == 14 &&
                physical.Colour == new MatroskaColourSettings() && physical.Projection?.Id == 0x7670);
            MatroskaVideoSettings huge = await VideoAsync([.. Unsigned(0xB0, ulong.MaxValue), .. Unsigned(0xBA, 2), .. Unsigned(0x54CC, ulong.MaxValue - 1)]);
            Require(huge.DisplayWidth == 1 && huge.DisplayHeight == 2);
            Require((await AudioAsync([.. Field(0x78B5, []), .. Float(0xB5, 44100.5)])).OutputSamplingFrequency == 44100.5);
        }

        public static async Task VerifyInvalidInfoAsync()
        {
            await RejectAsync<InvalidDataException>(async () => await InfoAsync([]));
            await RejectAsync<InvalidDataException>(async () => await InfoAsync(Field(0x4D80, [])));
            foreach (byte[] field in new[] { Unsigned(0x2AD7B1, 0), Field(0x2AD7B1, new byte[9]), Float(0x4489, 0),
                Float(0x4489, double.NaN), Float(0x4489, double.PositiveInfinity), Float(0x4489, -1), Field(0x4489, []),
                Field(0x4489, new byte[3]), Field(0x7BA9, [255]), Field(0x73A4, new byte[16]), Field(0x3CB923, new byte[15]) })
            {
                await RejectAsync<InvalidDataException>(async () => await InfoAsync([.. InfoFields, .. field]));
            }
            for (int size = 1; size <= 9; size++)
            {
                if (size != 8)
                {
                    await RejectAsync<InvalidDataException>(async () => await InfoAsync([.. InfoFields, .. Field(0x4461, new byte[size])]));
                }
            }
            foreach (byte[] field in new[] { Unsigned(0x2AD7B1, 1), Float(0x4489, 1), Field(0x4461, []), Field(0x7BA9, []),
                Field(0x73A4, [1, .. new byte[15]]) })
            {
                await InfoAsync([.. InfoFields, .. field]);
                await RejectAsync<InvalidDataException>(async () => await InfoAsync([.. InfoFields, .. field, .. field]));
            }
            await RejectAsync<InvalidDataException>(async () => await InfoAsync([.. InfoFields, .. InfoFields]));
            byte[] id = [1, .. new byte[15]];
            await RejectAsync<InvalidDataException>(async () => await InfoAsync([.. InfoFields, .. Field(0x3CB923, id)]));
            await RejectAsync<InvalidDataException>(async () => await InfoAsync([.. InfoFields, .. Field(0x73A4, id), .. Field(0x3CB923, id)]));
            await RejectAsync<InvalidDataException>(async () => await InfoAsync([.. InfoFields, 0xEC, 255]));
        }

        public static async Task VerifyInvalidTracksAsync()
        {
            byte[][] required = [Unsigned(0xD7, 1), Unsigned(0x73C5, 2), Unsigned(0x83, 2), Field(0x86, "A_AAC"u8)];
            for (int missing = 0; missing < required.Length; missing++)
            {
                byte[] remaining = required.Where((_, index) => index != missing).SelectMany(static value => value).ToArray();
                await RejectAsync<InvalidDataException>(async () => await TracksAsync(Field(0xAE, remaining)));
            }
            foreach (byte[] field in new[] { Unsigned(0xB9, 2), Unsigned(0x88, 2), Unsigned(0x55AA, 2), Unsigned(0x9C, 2),
                Unsigned(0x23E383, 0), Float(0x23314F, 0), Float(0x23314F, double.NaN), Field(0x22B59C, [255]) })
            {
                await RejectAsync<InvalidDataException>(async () => await TracksAsync(Field(0xAE, [.. TrackFields(), .. field])));
            }
            foreach (byte[] field in new[] { Field(0x63A2, []), Field(0xB9, []), Field(0x88, []), Field(0x55AA, []), Field(0x9C, []),
                Unsigned(0x23E383, 1), Field(0x23314F, []), Field(0x56AA, []), Field(0x56BB, []), Field(0x536E, []),
                Field(0x22B59C, []), Field(0x22B59D, "en"u8), Field(0xE1, []), Field(0xE0, [.. Unsigned(0xB0, 1), .. Unsigned(0xBA, 1)]),
                Field(0x6D80, []), Field(0xE2, []) })
            {
                await TracksAsync(Field(0xAE, [.. TrackFields(), .. field]));
                await RejectAsync<InvalidDataException>(async () => await TracksAsync(Field(0xAE, [.. TrackFields(), .. field, .. field])));
            }
            foreach (byte[] field in required)
            {
                await RejectAsync<InvalidDataException>(async () => await TracksAsync(Field(0xAE, [.. TrackFields(), .. field])));
            }
            await RejectAsync<InvalidDataException>(async () => await TracksAsync([]));
            byte[] duplicate = [.. Field(0xAE, TrackFields()), .. Field(0xAE, TrackFields())];
            await RejectAsync<InvalidDataException>(async () => await TracksAsync(duplicate));
            await RejectAsync<NotSupportedException>(async () => await TracksAsync(duplicate, new(1, 100, 100)));
        }

        public static async Task VerifyInvalidSettingsAsync()
        {
            foreach (byte[] field in new[] { Unsigned(0x9F, 0), Unsigned(0x6264, 0), Float(0xB5, 0),
                Float(0xB5, double.NaN), Float(0x78B5, double.NegativeInfinity), Float(0x78B5, -1) })
            {
                await RejectAsync<InvalidDataException>(async () => await AudioAsync(field));
            }
            foreach (byte[] field in new[] { Field(0xB5, []), Field(0x78B5, []), Field(0x9F, []), Unsigned(0x6264, 1) })
            {
                await AudioAsync(field);
                await RejectAsync<InvalidDataException>(async () => await AudioAsync([.. field, .. field]));
            }
            byte[] dimensions = [.. Unsigned(0xB0, 16), .. Unsigned(0xBA, 16)];
            foreach (byte[] fields in new[] { Array.Empty<byte>(), Unsigned(0xB0, 16),
                [.. dimensions, .. Unsigned(0x54CC, ulong.MaxValue)], [.. dimensions, .. Unsigned(0x54BB, 16)],
                [.. dimensions, .. Unsigned(0x54CC, 15), .. Unsigned(0x54DD, 1)], [.. dimensions, .. Unsigned(0x9A, 3)],
                [.. dimensions, .. Unsigned(0x9A, 1), .. Unsigned(0x9D, 5)],
                [.. dimensions, .. Field(0x54B0, []), .. Unsigned(0x54B2, 3)], [.. dimensions, .. Unsigned(0x54BA, 0)] })
            {
                await RejectAsync<InvalidDataException>(async () => await VideoAsync(fields));
            }
        }
    }
}

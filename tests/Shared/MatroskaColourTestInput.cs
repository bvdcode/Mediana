// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Matroska;
using Mediana.Core.Video;
using static Mediana.Tests.MatroskaMetadataTestData;

namespace Mediana.Tests
{
    internal static class MatroskaColourTestInput
    {
        public static async Task VerifyNativeFieldsAsync()
        {
            byte[] fields = [];
            for (uint field = 0x55B1; field <= 0x55BD; field++)
            {
                fields = [.. fields, .. Unsigned(field, field)];
            }
            MatroskaVideoSettings video = await VideoAsync([.. Unsigned(0xB0, 64), .. Unsigned(0xBA, 48),
                .. Unsigned(0x53B8, 3), .. Unsigned(0x53C0, 1), .. Field(0x55B0, [.. fields, .. Field(0x55D0, [])])]);
            MatroskaColourSettings expected = new()
            {
                MatrixCoefficients = 0x55B1,
                BitsPerChannel = 0x55B2,
                ChromaSubsamplingHorz = 0x55B3,
                ChromaSubsamplingVert = 0x55B4,
                CbSubsamplingHorz = 0x55B5,
                CbSubsamplingVert = 0x55B6,
                ChromaSitingHorz = 0x55B7,
                ChromaSitingVert = 0x55B8,
                Range = 0x55B9,
                TransferCharacteristics = 0x55BA,
                Primaries = 0x55BB,
                MaxCll = 0x55BC,
                MaxFall = 0x55BD,
                HasMasteringMetadata = true
            };
            Require(video.HasColourDescription && video.Colour == expected && video.StereoMode == 3 && video.AlphaMode == 1);
            foreach (uint field in Enumerable.Range(0x55B1, 13).Select(value => (uint)value).Append(0x55D0u))
            {
                byte[] value = field == 0x55D0 ? Field(field, []) : Unsigned(field, 0);
                await RejectAsync<InvalidDataException>(async () => await VideoAsync([.. Unsigned(0xB0, 1),
                    .. Unsigned(0xBA, 1), .. Field(0x55B0, [.. value, .. value])]));
            }
        }

        public static async Task VerifyColorResolutionAsync()
        {
            VideoColorInfo codec = new(ColorPrimaries.Bt709, TransferFunction.Bt709,
                MatrixCoefficients.Bt709, ColorRange.Limited, ChromaLocation.Left);
            Require(new MatroskaColorDescription(new()).Resolve(codec) == codec);
            foreach ((ulong h, ulong v, ChromaLocation expected) in new[]
            {
                (1UL, 1UL, ChromaLocation.TopLeft), (1UL, 2UL, ChromaLocation.Left),
                (2UL, 1UL, ChromaLocation.Top), (2UL, 2UL, ChromaLocation.Center)
            })
            {
                MatroskaColourSettings settings = new() { ChromaSitingHorz = h, ChromaSitingVert = v, Range = 2 };
                VideoColorInfo resolved = new MatroskaColorDescription(settings).Resolve(codec);
                Require(resolved == codec with { ChromaLocation = expected, Range = ColorRange.Full });
            }
            Require(new MatroskaColorDescription(new() { ChromaSitingHorz = 2 }).Resolve(
                codec with { ChromaLocation = ChromaLocation.BottomLeft }).ChromaLocation == ChromaLocation.Bottom);
            Require(new MatroskaColorDescription(new() { ChromaSitingHorz = 1 }).Resolve(new()).ChromaLocation == ChromaLocation.Unspecified);
            MatroskaColourSettings cicp = new() { Primaries = 6, TransferCharacteristics = 6, MatrixCoefficients = 6, Range = 1 };
            Require(new MatroskaColorDescription(cicp).Resolve(codec) == new CicpColorDescription(6, 6, 6, false).Resolve(codec));
            foreach (MatroskaColourSettings unsupported in new[]
            {
                new MatroskaColourSettings { Range = 3 }, new() { ChromaSitingHorz = 3 },
                new() { ChromaSitingVert = 3 }, new() { MatrixCoefficients = 3 }, new() { Primaries = ulong.MaxValue }
            })
            {
                await RejectAsync<NotSupportedException>(() =>
                {
                    new MatroskaColorDescription(unsupported).Resolve(codec);
                    return Task.CompletedTask;
                });
            }
        }

        public static void VerifyPresentationMapping()
        {
            MatroskaVideoSettings video = new() { PixelWidth = 720, PixelHeight = 576, DisplayUnit = 3, DisplayWidth = 4, DisplayHeight = 3 };
            Require(MatroskaVideoMapping.TryGetAspectRatio(video, out VideoAspectRatioDescription? aspect));
            Require(aspect!.Value == new SampleAspectRatio(16, 15));
            foreach (MatroskaVideoSettings unsupported in new[]
            {
                video with { CropLeft = 1 }, video with { Interlaced = 1 }, video with { DisplayUnit = 4 },
                video with { StereoMode = 1 }, video with { AlphaMode = 1 },
                video with { Projection = new(0x7670, 0, 0) }, video with { DisplayWidth = null },
                video with { Colour = new() { BitsPerChannel = 10 } },
                video with { Colour = new() { ChromaSubsamplingVert = 0 } },
                video with { Colour = new() { CbSubsamplingHorz = 1 } }
            })
            {
                Require(!MatroskaVideoMapping.TryGetAspectRatio(unsupported, out _));
            }
            Require(MatroskaVideoMapping.TryGetAspectRatio(new() { PixelWidth = ulong.MaxValue, PixelHeight = ulong.MaxValue }, out aspect));
            Require(aspect!.Value == SampleAspectRatio.Square);
        }
    }
}

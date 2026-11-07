// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Formats.Video
{
    /// <summary>Standard sample aspect ratio identifiers shared by AVC and HEVC VUI.</summary>
    public static class SampleAspectRatioCode
    {
        /// <summary>Returns null for unspecified (zero). Identifier 255 requires the explicitly coded ratio.</summary>
        public static SampleAspectRatio? Get(int identifier) => identifier switch
        {
            0 => null,
            1 => new(1, 1),
            2 => new(12, 11),
            3 => new(10, 11),
            4 => new(16, 11),
            5 => new(40, 33),
            6 => new(24, 11),
            7 => new(20, 11),
            8 => new(32, 11),
            9 => new(80, 33),
            10 => new(18, 11),
            11 => new(15, 11),
            12 => new(64, 33),
            13 => new(160, 99),
            14 => new(4, 3),
            15 => new(3, 2),
            16 => new(2, 1),
            255 => throw new InvalidDataException("An extended sample aspect ratio requires its coded width and height."),
            _ => throw new InvalidDataException("The sample aspect ratio identifier is reserved.")
        };
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Formats.Video
{
    public record H264VuiParameters
    {
        public int AspectRatioWidth { get; init; }
        public int AspectRatioHeight { get; init; }
        public bool? OverscanAppropriate { get; init; }
        public int VideoFormat { get; init; } = 5;
        public bool FullRange { get; init; }
        public int ColorPrimaries { get; init; } = 2;
        public int TransferCharacteristics { get; init; } = 2;
        public int MatrixCoefficients { get; init; } = 2;
        public int? ChromaLocationTop { get; init; }
        public int? ChromaLocationBottom { get; init; }
        public uint? UnitsInTick { get; init; }
        public uint? TimeScale { get; init; }
        public bool FixedFrameRate { get; init; }
        public H264HrdTiming? NalHrdTiming { get; init; }
        public H264HrdTiming? VclHrdTiming { get; init; }
        public bool PictureStructurePresent { get; init; }
        public int MaxMotionLengthHorizontal { get; init; } = 15;
        public int MaxMotionLengthVertical { get; init; } = 15;
        public int? MaxReorderFrames { get; init; }
        public int? MaxDecodedFrameBuffering { get; init; }

        /// <summary>Converts a chroma sample location code into its spatial position.</summary>
        public static ChromaLocation GetChromaLocation(int code) => code switch
        {
            0 => ChromaLocation.Left,
            1 => ChromaLocation.Center,
            2 => ChromaLocation.TopLeft,
            3 => ChromaLocation.Top,
            4 => ChromaLocation.BottomLeft,
            5 => ChromaLocation.Bottom,
            _ => throw new InvalidDataException("Invalid H.264 chroma location code.")
        };

        internal static int GetChromaLocationCode(ChromaLocation location) => location switch
        {
            ChromaLocation.Left => 0,
            ChromaLocation.Center => 1,
            ChromaLocation.TopLeft => 2,
            ChromaLocation.Top => 3,
            ChromaLocation.BottomLeft => 4,
            ChromaLocation.Bottom => 5,
            _ => throw new NotSupportedException("The source chroma location cannot be represented in H.264 SPS parameters.")
        };
    }
}

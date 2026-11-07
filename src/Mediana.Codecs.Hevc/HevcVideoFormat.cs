// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Codecs.Hevc
{
    internal static class HevcVideoFormat
    {
        public static VideoFormat Create(SequenceParameters sequence, VideoColorDescription? color,
            VideoAspectRatioDescription? aspect)
        {
            if (sequence.ChromaFormat != 1) { throw new NotSupportedException("HEVC video output requires YUV 4:2:0 samples."); }
            VideoPixelFormat pixels = sequence.BitDepth switch
            {
                8 => VideoPixelFormat.Yuv420P8,
                10 => VideoPixelFormat.Yuv420P10,
                _ => throw new NotSupportedException("HEVC video output requires eight or ten-bit components.")
            };
            VideoInformation video = sequence.Video;
            if (video.ChromaLocation != video.BottomChromaLocation)
            { throw new NotSupportedException("Different top and bottom chroma locations are not represented in progressive HEVC output."); }
            ChromaLocation location = video.ChromaLocation switch
            {
                0 => ChromaLocation.Left,
                1 => ChromaLocation.Center,
                2 => ChromaLocation.TopLeft,
                3 => ChromaLocation.Top,
                4 => ChromaLocation.BottomLeft,
                5 => ChromaLocation.Bottom,
                _ => throw new InvalidDataException("Invalid HEVC chroma location.")
            };
            VideoColorInfo labels = new(CicpColorimetry.GetPrimaries(video.ColourPrimaries),
                CicpColorimetry.GetTransfer(video.TransferCharacteristics), CicpColorimetry.GetMatrix(video.MatrixCoefficients),
                video.FullRange ? ColorRange.Full : ColorRange.Limited, location);
            SampleAspectRatio? resolvedAspect = aspect is null ? video.AspectRatio : aspect.Resolve(video.AspectRatio);
            return new(sequence.VisibleWidth, sequence.VisibleHeight, pixels, labels, resolvedAspect, color);
        }
    }
}

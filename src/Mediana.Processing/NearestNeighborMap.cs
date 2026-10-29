// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using Mediana.Core.Video;

namespace Mediana.Processing
{
    internal readonly record struct NearestNeighborMap(int SourceWidth, int SourceHeight, int Width, int Height,
        OrthogonalTransform Orientation)
    {
        public int OutputWidth => Orientation.SwapsAxes ? Height : Width;
        public int OutputHeight => Orientation.SwapsAxes ? Width : Height;

        public int SourceX(int x, int y) => (int)((long)Orientation.SourceX(x, y, Width) * SourceWidth / Width);
        public int SourceY(int x, int y) => (int)((long)Orientation.SourceY(x, y, Height) * SourceHeight / Height);

        public static NearestNeighborMap Fit(VideoFormat input, SampleAspectRatio aspectRatio, OrthogonalTransform orientation,
            int maximumWidth, int maximumHeight)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumWidth);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumHeight);
            aspectRatio.Validate();
            int widthLimit = orientation.SwapsAxes ? maximumHeight : maximumWidth;
            int heightLimit = orientation.SwapsAxes ? maximumWidth : maximumHeight;
            BigInteger displayWidth = (BigInteger)input.Width * aspectRatio.Numerator;
            BigInteger displayHeight = (BigInteger)input.Height * aspectRatio.Denominator;
            int boxWidth = RoundWithin(displayWidth, aspectRatio.Denominator, widthLimit);
            int boxHeight = Math.Min(input.Height, heightLimit);
            int width;
            int height;
            if (displayWidth * boxHeight >= displayHeight * boxWidth)
            {
                width = boxWidth;
                height = RoundWithin(width * displayHeight, displayWidth, boxHeight);
            }
            else
            {
                height = boxHeight;
                width = RoundWithin(height * displayWidth, displayHeight, boxWidth);
            }
            return new NearestNeighborMap(input.Width, input.Height, width, height, orientation);
        }

        private static int RoundWithin(BigInteger numerator, BigInteger denominator, int maximum) =>
            (int)BigInteger.Clamp((numerator * 2 + denominator) / (denominator * 2), BigInteger.One, maximum);
    }
}

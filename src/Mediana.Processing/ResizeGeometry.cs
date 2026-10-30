// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using Mediana.Core.Video;

namespace Mediana.Processing
{
    internal static class ResizeGeometry
    {
        public static SampleAspectRatio? PreserveDisplayAspect(VideoFormat source, int width, int height)
        {
            if (source.SampleAspectRatio is not SampleAspectRatio aspect)
            {
                return null;
            }
            BigInteger numerator = (BigInteger)aspect.Numerator * source.Width * height;
            BigInteger denominator = (BigInteger)aspect.Denominator * source.Height * width;
            BigInteger divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
            numerator /= divisor;
            denominator /= divisor;
            if (numerator > int.MaxValue || denominator > int.MaxValue)
            {
                throw new NotSupportedException("The exact resized sample aspect ratio exceeds its signed 32-bit representation.");
            }
            return new SampleAspectRatio((int)numerator, (int)denominator);
        }

        public static (int Width, int Height) FitEven(VideoFormat source, int maximumWidth, int maximumHeight)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(maximumWidth, 2);
            ArgumentOutOfRangeException.ThrowIfLessThan(maximumHeight, 2);
            int width = Math.Min(source.Width, maximumWidth) & ~1;
            int height = Math.Min(source.Height, maximumHeight) & ~1;
            if ((long)source.Width * height > (long)source.Height * width)
            {
                height = Math.Max(2, (int)((long)source.Height * width / source.Width) & ~1);
            }
            else
            {
                width = Math.Max(2, (int)((long)source.Width * height / source.Height) & ~1);
            }
            return (width, height);
        }
    }
}

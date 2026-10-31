// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using Mediana.Core.Video;

namespace Mediana.Containers.Mp4
{
    internal static class Mp4AspectRatioParser
    {
        private static readonly VideoAspectRatioDescription QuickTimeDefault = new(SampleAspectRatio.Square, isDefault: true);

        public static VideoAspectRatioDescription? Read(Mp4BoxCollection boxes, bool quickTime)
        {
            Mp4Box? box = boxes.Optional("pasp");
            if (box is null)
            {
                if (quickTime)
                {
                    return QuickTimeDefault;
                }
                return null;
            }
            ReadOnlySpan<byte> payload = box.Value.Payload.Span;
            if (payload.Length != 8)
            {
                throw new InvalidDataException("The MP4 pixel aspect ratio requires two 32-bit spacing values.");
            }
            uint horizontal = Mp4Binary.UInt32(payload, 0);
            uint vertical = Mp4Binary.UInt32(payload, 4);
            if (horizontal == 0 || vertical == 0)
            {
                throw new InvalidDataException("The MP4 pixel spacing values must be nonzero.");
            }
            uint divisor = (uint)BigInteger.GreatestCommonDivisor(horizontal, vertical);
            horizontal /= divisor;
            vertical /= divisor;
            if (horizontal > int.MaxValue || vertical > int.MaxValue)
            {
                throw new NotSupportedException("The reduced MP4 pixel aspect ratio exceeds its signed 32-bit representation.");
            }
            return new VideoAspectRatioDescription(new SampleAspectRatio((int)horizontal, (int)vertical));
        }
    }
}

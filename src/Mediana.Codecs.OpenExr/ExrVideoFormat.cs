// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Numerics;
using Mediana.Core.Video;

namespace Mediana.Codecs.OpenExr
{
    internal static class ExrVideoFormat
    {
        internal static VideoFormat Create(ExrHeader header)
        {
            VideoColorInfo color = VideoColorInfo.Srgb with { Transfer = TransferFunction.Linear };
            VideoColorDescription? description = null;
            if (header.Attributes.TryGetValue("chromaticities", out ExrAttribute attribute))
            {
                if (attribute.Type != "chromaticities" || attribute.Data.Length != 32)
                { throw new InvalidDataException("Invalid OpenEXR chromaticities."); }
                float Value(int index) => BinaryPrimitives.ReadSingleLittleEndian(attribute.Data.Span[(index * 4)..]);
                description = new RgbChromaticitiesColorDescription(new(Value(0), Value(1), Value(2), Value(3), Value(4), Value(5), Value(6), Value(7)));
                color = color with { Primaries = ColorPrimaries.Unspecified };
            }
            return new(header.DataWindow.Width, header.DataWindow.Height, VideoPixelFormat.RgbaFloat32Premultiplied,
                color, Aspect(header.PixelAspectRatio), description);
        }

        private static SampleAspectRatio Aspect(float value)
        {
            uint bits = BitConverter.SingleToUInt32Bits(value);
            int exponent = (int)((bits >> 23) & 255) - 150;
            uint numerator = (bits & 0x7fffff) | 0x800000;
            if ((bits & 0x7f800000) == 0) { numerator &= 0x7fffff; exponent = -149; }
            int zeros = BitOperations.TrailingZeroCount(numerator);
            numerator >>= zeros; exponent += zeros;
            if (exponent >= 0)
            {
                if (exponent > 30 || numerator > (uint)int.MaxValue >> exponent)
                { throw new NotSupportedException("OpenEXR pixel aspect ratio exceeds the integer ratio range."); }
                return new((int)(numerator << exponent), 1);
            }
            if (exponent < -30) { throw new NotSupportedException("OpenEXR pixel aspect ratio exceeds the integer ratio range."); }
            return new((int)numerator, 1 << -exponent);
        }
    }
}

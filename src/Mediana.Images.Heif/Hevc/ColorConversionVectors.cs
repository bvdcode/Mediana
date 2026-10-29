// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Mediana.Images.Heif.Hevc
{
    /// <summary>
    /// Converts reconstructed 8/10-bit samples in vector lanes with exact Q20 rounding.
    /// </summary>
    /// <remarks>
    /// For supported matrices and sample ranges, accumulator magnitudes stay below 2^30.
    /// Clearing Q20 fractions leaves exact multiples of 2^20 with eight significant bits.
    /// Adding 2^43 to their float representation places the channel in the lowest eight
    /// mantissa bits. Lane packing follows
    /// native byte order so a byte view always contains red, green, blue and alpha.
    /// </remarks>
    internal readonly struct ColorConversionVectors(int lumaOffset, int chromaCenter, long lumaScale,
        long redScale, long blueScale, long greenBlueScale, long greenRedScale)
    {
        private const int FractionBits = 20;
        private const int FloatFractionBits = 23;
        private const int Scale = 1 << FractionBits;
        private static readonly Vector<int> Rounding = new Vector<int>(Scale / 2);
        private static readonly Vector<int> Maximum = new Vector<int>(255 * Scale);
        private static readonly Vector<int> WholeBits = new Vector<int>(-Scale);
        private static readonly Vector<float> ChannelBias = new Vector<float>(1L << (FractionBits + FloatFractionBits));
        private static readonly Vector<int> ChannelBits = new Vector<int>(255);
        private static readonly Vector<int> RedWeight = new Vector<int>(BitConverter.IsLittleEndian ? 1 : 1 << 24);
        private static readonly Vector<int> GreenWeight = new Vector<int>(BitConverter.IsLittleEndian ? 1 << 8 : 1 << 16);
        private static readonly Vector<int> BlueWeight = new Vector<int>(BitConverter.IsLittleEndian ? 1 << 16 : 1 << 8);
        private static readonly Vector<int> Alpha = new Vector<int>(BitConverter.IsLittleEndian ? unchecked((int)0xFF000000) : 255);
        private readonly Vector<int> yOffset = new Vector<int>(lumaOffset);
        private readonly Vector<int> chromaOffset = new Vector<int>(chromaCenter);
        private readonly Vector<int> yFactor = new Vector<int>(checked((int)lumaScale));
        private readonly Vector<int> redCr = new Vector<int>(checked((int)redScale));
        private readonly Vector<int> blueCb = new Vector<int>(checked((int)blueScale));
        private readonly Vector<int> greenCb = new Vector<int>(checked((int)greenBlueScale));
        private readonly Vector<int> greenCr = new Vector<int>(checked((int)greenRedScale));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector<int> Convert(Vector<int> y, Vector<int> cb, Vector<int> cr)
        {
            Vector<int> luma = yFactor * (y - yOffset);
            Vector<int> blue = cb - chromaOffset;
            Vector<int> red = cr - chromaOffset;
            Vector<int> r = Channel(luma + (redCr * red)) * RedWeight;
            Vector<int> g = Channel(luma + (greenCb * blue) + (greenCr * red)) * GreenWeight;
            Vector<int> b = Channel(luma + (blueCb * blue)) * BlueWeight;
            return r | g | b | Alpha;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector<int> Channel(Vector<int> value)
        {
            Vector<int> clipped = Vector.Min(Maximum, Vector.Max(Vector<int>.Zero, value + Rounding));
            Vector<int> integral = clipped & WholeBits;
            return Vector.AsVectorInt32(Vector.ConvertToSingle(integral) + ChannelBias) & ChannelBits;
        }
    }
}

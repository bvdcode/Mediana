// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Processing
{
    internal static class ResizeTransfer
    {
        private const int LinearResolution = 65535;
        private static readonly float[] Linear = Enumerable.Range(0, 256)
            .Select(value => (float)SdrTransfer.Decode(value / 255.0, TransferFunction.Srgb)).ToArray();
        private static readonly byte[] Encoded = Enumerable.Range(0, LinearResolution + 1)
            .Select(value => SdrTransfer.EncodeSrgb(value / (double)LinearResolution)).ToArray();

        public static float Decode(byte value, bool linear) => linear ? Linear[value] : value / 255f;
        public static byte Encode(float value, bool linear) => linear
            ? Encoded[(int)(Math.Clamp(value, 0, 1) * LinearResolution + 0.5f)]
            : Byte(value);
        public static byte Byte(float value) => (byte)(Math.Clamp(value, 0, 1) * 255 + 0.5f);
    }
}

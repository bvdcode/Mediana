// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Processing
{
    internal static class IccSrgbEncoding
    {
        private static readonly byte[] Values = Create();
        internal static byte Encode(float linear) => Values[(int)(Math.Clamp(linear, 0, 1) * (Values.Length - 1) + 0.5f)];
        private static byte[] Create()
        {
            byte[] values = new byte[ushort.MaxValue + 1];
            for (int i = 0; i < values.Length; i++) { values[i] = SdrTransfer.EncodeSrgb((double)i / ushort.MaxValue); }
            return values;
        }
    }
}

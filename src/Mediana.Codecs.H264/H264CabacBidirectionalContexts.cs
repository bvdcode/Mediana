// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    /// <summary>Initializes B-slice skip and partition contexts under H.264 Table 9-14.</summary>
    internal static class H264CabacBidirectionalContexts
    {
        private const int Count = 16;
        private static ReadOnlySpan<sbyte> InitialValues =>
        [
            18, 64, 9, 43, 29, 0, 26, 67, 16, 90, 9, 104, -46, 127, -20, 104,
            1, 67, -13, 78, -11, 65, 1, 62, -6, 86, -17, 95, -6, 61, 9, 45,
            26, 34, 19, 22, 40, 0, 57, 2, 41, 36, 26, 69, -45, 127, -15, 101,
            -4, 76, -6, 71, -13, 79, 5, 52, 6, 69, -13, 90, 0, 52, 8, 43,
            20, 40, 20, 10, 29, 0, 54, 0, 37, 42, 12, 97, -32, 127, -22, 117,
            -2, 74, -4, 85, -24, 102, 5, 57, -6, 93, -14, 88, -6, 44, 4, 55
        ];

        public static H264CabacContext[] Create(int qp, int initialization)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(initialization);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(initialization, 2);
            H264CabacContext[] result = new H264CabacContext[Count];
            int offset = initialization * Count * 2;
            for (int index = 0; index < result.Length; index++)
            {
                result[index] = H264CabacContext.Initialize(InitialValues[offset + index * 2], InitialValues[offset + index * 2 + 1], qp);
            }
            return result;
        }
    }
}

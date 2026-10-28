namespace Mediana.Codecs.H264
{
    /// <summary>Initializes progressive 8x8 residual contexts 402..435 from H.264 Table 9-24.</summary>
    internal static class H264CabacTransformContexts
    {
        public const int Count = 34;
        private static readonly sbyte[] InitialValues =
        [
            -17, 120, -20, 112, -18, 114, -11, 85, -15, 92, -14, 89, -26, 71, -15, 81,
            -14, 80, 0, 68, -14, 70, -24, 56, -23, 68, -24, 50, -11, 74, 23, -13,
            26, -13, 40, -15, 49, -14, 44, 3, 45, 6, 44, 34, 33, 54, 19, 82,
            -3, 75, -1, 23, 1, 34, 1, 43, 0, 54, -2, 55, 0, 61, 1, 64,
            0, 68, -9, 92,
            -4, 79, -7, 71, -5, 69, -9, 70, -8, 66, -10, 68, -19, 73, -12, 69,
            -16, 70, -15, 67, -20, 62, -19, 70, -16, 66, -22, 65, -20, 63, 9, -2,
            26, -9, 33, -9, 39, -7, 41, -2, 45, 3, 49, 9, 45, 27, 36, 59,
            -6, 66, -7, 35, -7, 42, -8, 45, -5, 48, -12, 56, -6, 60, -5, 62,
            -8, 66, -8, 76,
            -5, 85, -6, 81, -10, 77, -7, 81, -17, 80, -18, 73, -4, 74, -10, 83,
            -9, 71, -9, 67, -1, 61, -8, 66, -14, 66, 0, 59, 2, 59, 17, -10,
            32, -13, 42, -9, 49, -5, 53, 0, 64, 3, 68, 10, 66, 27, 47, 57,
            -5, 71, 0, 24, -1, 36, -2, 42, -2, 52, -9, 57, -6, 63, -4, 65,
            -4, 67, -7, 82,
            -3, 78, -8, 74, -9, 72, -10, 72, -18, 75, -12, 71, -11, 63, -5, 70,
            -17, 75, -14, 72, -16, 67, -8, 53, -14, 59, -9, 52, -11, 68, 9, -2,
            30, -10, 31, -4, 33, -1, 33, 7, 31, 12, 37, 23, 31, 38, 20, 64,
            -9, 71, -7, 37, -8, 44, -11, 49, -10, 56, -12, 59, -8, 63, -9, 67,
            -6, 68, -10, 79,
        ];

        public static H264CabacContext[] Create(int qp, H264SliceKind kind, int initialization)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(initialization);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(initialization, 2);
            int selection = kind switch
            {
                H264SliceKind.I => 0,
                H264SliceKind.P => initialization + 1,
                _ => throw new NotSupportedException("8x8 CABAC contexts support I and P slices.")
            };
            H264CabacContext[] contexts = new H264CabacContext[Count];
            int offset = selection * Count * 2;
            for (int index = 0; index < Count; index++)
            {
                contexts[index] = H264CabacContext.Initialize(InitialValues[offset + index * 2], InitialValues[offset + index * 2 + 1], qp);
            }
            return contexts;
        }
    }
}

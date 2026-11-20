// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264IntraPrediction
    {
        public static void Luma4x4(int mode, ReadOnlySpan<byte> top, ReadOnlySpan<byte> left, byte? corner, Span<byte> output) =>
            H264IntraPrediction<byte>.Luma4x4(mode, top, left, corner, output, byte.MaxValue);

        public static void Luma8x8(int mode, ReadOnlySpan<byte> top, ReadOnlySpan<byte> left, byte? corner, Span<byte> output) =>
            H264IntraPrediction<byte>.Luma8x8(mode, top, left, corner, output, byte.MaxValue);

        public static void Luma16x16(int mode, ReadOnlySpan<byte> top, ReadOnlySpan<byte> left, byte? corner, Span<byte> output) =>
            H264IntraPrediction<byte>.Luma16x16(mode, top, left, corner, output, byte.MaxValue);

        public static void Chroma8x8(int mode, ReadOnlySpan<byte> top, ReadOnlySpan<byte> left, byte? corner, Span<byte> output) =>
            Chroma(mode, top, left, corner, output, 8);

        public static void Chroma(int mode, ReadOnlySpan<byte> top, ReadOnlySpan<byte> left, byte? corner, Span<byte> output, int height) =>
            H264IntraPrediction<byte>.Chroma(mode, top, left, corner, output, height, byte.MaxValue);

        public static bool IsLumaModeAvailable(int mode, bool hasTop, bool hasLeft, bool hasCorner) =>
            H264IntraPrediction<byte>.IsLumaModeAvailable(mode, hasTop, hasLeft, hasCorner);
    }
}

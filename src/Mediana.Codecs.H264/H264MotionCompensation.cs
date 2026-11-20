// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;

namespace Mediana.Codecs.H264
{
    internal static class H264MotionCompensation
    {
        public static void Luma(H264Picture reference, int x, int y, int motionX, int motionY,
            int width, int height, Span<byte> output)
        {
            ValidateOutput(width, height, output, reference.SampleBytes);
            ReadOnlySpan<byte> samples = reference.ReadPlane(0);
            long sourceX = (long)x + (motionX >> 2);
            long sourceY = (long)y + (motionY >> 2);
            int dx = motionX & 3;
            int dy = motionY & 3;
            if (reference.SampleBytes != 1)
            {
                ReadOnlySpan<ushort> pixels = MemoryMarshal.Cast<byte, ushort>(samples);
                Span<ushort> destination = MemoryMarshal.Cast<byte, ushort>(output);
                if ((dx | dy) == 0)
                {
                    H264HighPrecisionInterpolation.Copy(pixels, reference.Width, reference.Width, reference.Height, sourceX, sourceY, width, height, destination);
                }
                else
                {
                    H264HighPrecisionInterpolation.Luma(pixels, reference.Width, reference.Width, reference.Height, sourceX, sourceY,
                        dx, dy, width, height, destination, reference.MaximumSample);
                }
                return;
            }
            if ((dx | dy) == 0)
            {
                H264BorderExtension.Copy(samples, reference.Width, reference.Height, sourceX, sourceY, width, height, output);
                return;
            }
            H264LumaInterpolation.Apply(samples, reference.Width, reference.Width, reference.Height, sourceX, sourceY, dx, dy, width, height, output);
        }

        public static void Chroma(H264Picture reference, int plane, int x, int y, int motionX, int motionY,
            int width, int height, Span<byte> output)
        {
            if (plane is not (1 or 2))
            {
                throw new ArgumentOutOfRangeException(nameof(plane));
            }
            ValidateOutput(width, height, output, reference.SampleBytes);
            ReadOnlySpan<byte> samples = reference.ReadPlane(plane);
            int stride = reference.Width / 2;
            int verticalShift = 2 + reference.ChromaVerticalScale / 2;
            int rows = reference.Height / reference.ChromaVerticalScale;
            long sourceY = (long)y + (motionY >> verticalShift);
            int dy = (motionY & ((1 << verticalShift) - 1)) << (3 - verticalShift);
            if (reference.SampleBytes != 1)
            {
                ReadOnlySpan<ushort> pixels = MemoryMarshal.Cast<byte, ushort>(samples);
                Span<ushort> destination = MemoryMarshal.Cast<byte, ushort>(output);
                long sourceX = (long)x + (motionX >> 3);
                if (((motionX & 7) | dy) == 0)
                {
                    H264HighPrecisionInterpolation.Copy(pixels, stride, stride, rows, sourceX, sourceY, width, height, destination);
                }
                else
                {
                    H264HighPrecisionInterpolation.Chroma(pixels, stride, stride, rows, sourceX, sourceY, motionX & 7, dy, width, height, destination);
                }
                return;
            }
            if (((motionX & 7) | dy) == 0)
            {
                H264BorderExtension.Copy(samples, stride, rows, (long)x + (motionX >> 3), sourceY, width, height, output);
                return;
            }
            H264ChromaInterpolation.Apply(samples, stride, stride, rows, (long)x + (motionX >> 3), sourceY,
                motionX & 7, dy, width, height, output);
        }

        private static void ValidateOutput(int width, int height, Span<byte> output, int sampleBytes)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            if ((long)width * height * sampleBytes != output.Length)
            {
                throw new ArgumentException("The H.264 prediction output must match its block dimensions.", nameof(output));
            }
        }
    }
}

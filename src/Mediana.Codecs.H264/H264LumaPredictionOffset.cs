// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Mediana.Core.Video;

namespace Mediana.Codecs.H264
{
    internal static class H264LumaPredictionOffset
    {
        private const int MaximumCorrection = 32;
        private const int SamplesAcrossShortAxis = 64;

        public static bool IsEnabled(H264EncoderOptions options) =>
            options.EntropyCoding == H264EntropyCoding.Cabac &&
            options.MacroblockAnalysis == H264MacroblockAnalysis.Fast &&
            options.RateControl is null && options.KeyFrameInterval > TimeSpan.Zero;

        public static int Estimate(VideoFrame input, H264Picture reference, CancellationToken token)
        {
            Span<int> histogram = stackalloc int[2 * MaximumCorrection + 1];
            histogram.Clear();
            int width = input.Format.Width;
            int height = input.Format.Height;
            int step = Math.Max(2, Math.Min(width, height) / SamplesAcrossShortAxis);
            int stride = reference.Stride(0);
            ReadOnlySpan<byte> pixels = reference.ReadPlane(0);
            int samples = 0;
            int originalError = 0;
            for (int y = step / 2; y < height; y += step)
            {
                token.ThrowIfCancellationRequested();
                ReadOnlySpan<byte> source = input.GetRow(0, y);
                ReadOnlySpan<byte> prior = pixels.Slice(y * stride, width);
                for (int x = step / 2; x < width; x += step)
                {
                    int difference = source[x] - prior[x];
                    histogram[Math.Clamp(difference, -MaximumCorrection, MaximumCorrection) + MaximumCorrection]++;
                    samples++;
                    originalError += Math.Abs(difference);
                }
            }
            int remaining = samples / 2;
            int offset = -MaximumCorrection;
            while (offset < MaximumCorrection && (remaining -= histogram[offset + MaximumCorrection]) >= 0)
            {
                offset++;
            }
            if (offset == 0)
            {
                return 0;
            }
            int reduction = 0;
            for (int difference = -MaximumCorrection; difference <= MaximumCorrection; difference++)
            {
                reduction += histogram[difference + MaximumCorrection] * (Math.Abs(difference) - Math.Abs(difference - offset));
            }
            const int MinimumRelativeReductionDivisor = 20;
            const int MinimumSampleReductionDivisor = 4;
            return reduction * MinimumRelativeReductionDivisor > originalError && reduction * MinimumSampleReductionDivisor >= samples
                && H264LumaOffsetMotionCheck.ImprovesPrediction(input, reference, offset, token)
                ? offset : 0;
        }

        public static void WriteTable(H264BitWriter bits, int offset)
        {
            bits.WriteUnsigned(0);
            bits.WriteUnsigned(0);
            bits.WriteFlag(true);
            bits.WriteSigned(1);
            bits.WriteSigned(offset);
            bits.WriteFlag(false);
        }

        public static void Apply(Span<byte> samples, int offset)
        {
            int i = 0;
            if (Vector128.IsHardwareAccelerated)
            {
                ref byte first = ref MemoryMarshal.GetReference(samples);
                Vector128<byte> delta = Vector128.Create((byte)Math.Abs(offset));
                for (; i <= samples.Length - Vector128<byte>.Count; i += Vector128<byte>.Count)
                {
                    Vector128<byte> source = Vector128.LoadUnsafe(ref first, (nuint)i);
                    Vector128<byte> result = offset > 0 ? Vector128.AddSaturate(source, delta) : Vector128.SubtractSaturate(source, delta);
                    result.StoreUnsafe(ref first, (nuint)i);
                }
            }
            for (; i < samples.Length; i++)
            {
                samples[i] = (byte)Math.Clamp(samples[i] + offset, byte.MinValue, byte.MaxValue);
            }
        }
    }
}

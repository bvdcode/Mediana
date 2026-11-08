// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats.Video;

namespace Mediana.Tests
{
    internal static class H264PreparedMacroblockTestInput
    {
        public static void Verify()
        {
            MediaBufferAllocator allocator = new(1_000_000);
            VideoFormat format = new(32, 32, VideoPixelFormat.Yuv420P8,
                new(ColorPrimaries.Bt709, TransferFunction.Bt709, MatrixCoefficients.Bt709, ColorRange.Limited, ChromaLocation.Center), new(1, 1));
            byte[] config = H264EncoderConfiguration.Create(format, allocator, new(), out _);
            H264SequenceParameterSet sequence = H264Configuration.Parse(config, new()).Sequences.Values.Single();
            Random random = new(1751);
            int checks = 0;
            using (H264Picture target = new(sequence, allocator, H264SliceKind.P))
            {
                byte[] sourceBytes = new byte[1536], prediction = new byte[384], samples = new byte[384];
                byte[] previous = new byte[1024];
                int[] coefficients = new int[384];
                byte[] reconstruction = new byte[384];
                foreach (int qp in new[] { 0, 6, 12, 22, 26, 30, 34, 42, 51 })
                    for (int repetition = 0; repetition < 32; repetition++)
                    {
                        random.NextBytes(sourceBytes); random.NextBytes(previous);
                        if (repetition % 3 == 1) { sourceBytes.AsSpan().Fill(0); }
                        if (repetition % 3 == 2) { sourceBytes.AsSpan().Fill(255); }
                        target.WriteZeroResidualBlock(0, 0, 0, 32, previous, 32);
                        target.WriteZeroResidualBlock(1, 0, 0, 16, previous, 16);
                        target.WriteZeroResidualBlock(2, 0, 0, 16, previous, 16);
                        target.Modes.Fill(H264Picture.InterMode);
                        using WritableMediaBuffer writable = allocator.Allocate(1536);
                        sourceBytes.CopyTo(writable.Memory.Span);
                        using MediaBuffer data = writable.Publish();
                        using VideoFrame input = new(format, data, format.CreateTightLayout());
                        H264SourceMacroblock source = new(input, 16, 16, samples);
                        for (int mode = -1; mode < 4; mode++)
                        {
                            int pattern;
                            if (mode < 0)
                            {
                                random.NextBytes(prediction);
                                pattern = H264InterEncoder.AnalyzeLuma(source.Luma, qp, prediction.AsSpan(0, 256), coefficients.AsSpan(0, 256));
                                pattern |= H264ChromaEncoder.Analyze(source.Chroma, qp, prediction.AsSpan(256), coefficients.AsSpan(256)) << 4;
                            }
                            else
                            { pattern = H264Intra16Encoder.Analyze(source, target, 16, 16, qp, mode, prediction, coefficients); }
                            long estimated = H264MacroblockReconstruction.Prepare(source, qp, pattern, prediction, coefficients, mode >= 0, reconstruction);
                            if (mode >= 0)
                            { H264Intra16Encoder.TryWrite(new H264CountingSyntax(), target, 16, 16, qp, pattern, prediction, coefficients); }
                            else
                            {
                                H264InterEncoder.TryWriteLuma(new H264CountingSyntax(), target, 16, 16, qp, pattern, prediction.AsSpan(0, 256), coefficients.AsSpan(0, 256));
                                H264ChromaEncoder.TryWrite(new H264CountingSyntax(), target, 8, 8, qp, pattern >> 4, prediction.AsSpan(256), coefficients.AsSpan(256));
                            }
                            long actual = 0;
                            for (int plane = 0; plane < 3; plane++)
                            {
                                int size = plane == 0 ? 16 : 8;
                                ReadOnlySpan<byte> reconstructed = target.ReadPlane(plane);
                                ReadOnlySpan<byte> original = source.Samples.Slice(plane == 0 ? 0 : 256 + (plane - 1) * 64, size * size);
                                for (int row = 0; row < size; row++)
                                    for (int column = 0; column < size; column++)
                                    {
                                        int actualSample = reconstructed[(size + row) * target.Stride(plane) + size + column];
                                        int sampleOffset = plane == 0 ? 0 : 256 + (plane - 1) * 64;
                                        if (reconstruction[sampleOffset + row * size + column] != actualSample)
                                        { throw new InvalidDataException("Prepared sample differs from actual writer"); }
                                        int difference = original[row * size + column] - actualSample;
                                        actual += (long)difference * difference;
                                    }
                            }
                            if (actual != estimated) { throw new InvalidDataException($"SSE mismatch QP={qp} mode={mode}: {actual} != {estimated}"); }
                            checks++;
                        }
                    }
            }
            if (allocator.AllocatedBytes != 0) { throw new InvalidDataException("Leaked oracle pictures"); }
            if (checks != 1440) { throw new InvalidDataException("Incomplete prepared reconstruction verification."); }

        }
    }
}

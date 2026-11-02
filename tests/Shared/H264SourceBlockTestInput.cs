// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    internal static class H264SourceBlockTestInput
    {
        private static ReadOnlySpan<int> Matrix => [1, 1, 1, 1, 2, 1, -1, -2, 1, -1, -1, 1, 1, -2, 2, -1];

        public static void Verify(int width, int height, bool padded)
        {
            MediaBufferAllocator allocator = new(64 * 1024);
            for (int pattern = 0; pattern < 3; pattern++)
            {
                using VideoFrame frame = Create(width, height, padded, pattern, allocator);
                byte[] unchanged = frame.Data.ToArray();
                for (int plane = 0; plane < 3; plane++)
                {
                    PlaneLayout layout = frame.Planes[plane];
                    foreach (int y in new[] { int.MinValue, -17, -1, 0, 1, layout.Height - 1, layout.Height, int.MaxValue })
                    {
                        foreach (int x in new[] { int.MinValue, -17, -1, 0, 1, layout.RowBytes - 1, layout.RowBytes, int.MaxValue })
                        {
                            foreach ((int blockWidth, int blockHeight) in new (int, int)[] { (1, 1), (4, 4), (8, 8), (16, 16), (3, 5) })
                            {
                                VerifyCopy(frame, plane, x, y, blockWidth, blockHeight, pattern);
                            }
                        }
                    }
                    foreach (int y in new[] { 0, 1, layout.Height - 1, layout.Height, (layout.Height + 15) / 16 * 16 })
                    {
                        foreach (int x in new[] { 0, 1, layout.RowBytes - 1, layout.RowBytes, (layout.RowBytes + 15) / 16 * 16 })
                        {
                            VerifyTransform(frame, plane, x, y, pattern);
                        }
                    }
                }
                VerifyMacroblocks(frame, pattern);
                Require(frame.Data.Span.SequenceEqual(unchanged));
            }
            Require(allocator.AllocatedBytes == 0);
        }

        private static void VerifyCopy(VideoFrame frame, int plane, int x, int y, int width, int height, int pattern)
        {
            PlaneLayout layout = frame.Planes[plane];
            Span<byte> guarded = stackalloc byte[258];
            guarded.Fill(0xcd);
            H264BorderExtension.Copy(frame, plane, x, y, width, height, guarded.Slice(1, width * height));
            Require(guarded[0] == 0xcd && guarded[1 + width * height] == 0xcd);
            for (int row = 0; row < height; row++)
            {
                for (int column = 0; column < width; column++)
                {
                    int sourceX = (int)Math.Clamp((long)x + column, 0, layout.RowBytes - 1);
                    int sourceY = (int)Math.Clamp((long)y + row, 0, layout.Height - 1);
                    Require(guarded[1 + row * width + column] == Pixel(plane, sourceX, sourceY, pattern));
                }
            }
        }

        private static void VerifyTransform(VideoFrame frame, int plane, int x, int y, int pattern)
        {
            PlaneLayout layout = frame.Planes[plane];
            Span<byte> prediction = stackalloc byte[16];
            Span<int> residual = stackalloc int[16];
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    int index = row * 4 + column;
                    prediction[index] = Pixel(plane, column + 17, row + 9, (pattern + 1) % 3);
                    residual[index] = Pixel(plane, Math.Min(x + column, layout.RowBytes - 1),
                        Math.Min(y + row, layout.Height - 1), pattern) - prediction[index];
                }
            }
            Span<int> guarded = stackalloc int[18];
            guarded.Fill(int.MinValue);
            Span<byte> source = stackalloc byte[16];
            H264BorderExtension.Copy(frame, plane, x, y, 4, 4, source);
            H264EncodingBlocks.Transform(source, 4, prediction, 4, guarded.Slice(1, 16));
            Require(guarded[0] == int.MinValue && guarded[^1] == int.MinValue);
            for (int vertical = 0; vertical < 4; vertical++)
            {
                for (int horizontal = 0; horizontal < 4; horizontal++)
                {
                    int expected = 0;
                    for (int row = 0; row < 4; row++)
                    {
                        for (int column = 0; column < 4; column++)
                        {
                            expected += Matrix[vertical * 4 + row] * residual[row * 4 + column] * Matrix[horizontal * 4 + column];
                        }
                    }
                    Require(guarded[1 + vertical * 4 + horizontal] == expected);
                }
            }
        }

        private static void VerifyMacroblocks(VideoFrame frame, int pattern)
        {
            byte[] guarded = new byte[H264SourceMacroblock.ByteCount + 6];
            foreach (int y in new[] { 0, 16, frame.Format.Height - 1, frame.Format.Height, int.MaxValue })
            {
                foreach (int x in new[] { 0, 16, frame.Format.Width - 1, frame.Format.Width, int.MaxValue })
                {
                    guarded.AsSpan().Fill(183);
                    H264SourceMacroblock source = new(frame, x, y, guarded.AsSpan(3, H264SourceMacroblock.ByteCount));
                    byte[] prepared = source.Samples.ToArray();
                    for (int plane = 0; plane < 3; plane++)
                    {
                        ReadOnlySpan<byte> samples = plane switch
                        {
                            0 => source.Luma,
                            1 => source.Chroma[..64],
                            2 => source.Chroma[64..],
                            _ => throw new ArgumentOutOfRangeException(nameof(plane))
                        };
                        int size = plane == 0 ? 16 : 8;
                        int scale = plane == 0 ? 1 : 2;
                        PlaneLayout layout = frame.Planes[plane];
                        byte[] expected = new byte[size * size];
                        for (int row = 0; row < size; row++)
                        {
                            for (int column = 0; column < size; column++)
                            {
                                int sourceX = (int)Math.Clamp((long)(x / scale) + column, 0, layout.RowBytes - 1);
                                int sourceY = (int)Math.Clamp((long)(y / scale) + row, 0, layout.Height - 1);
                                expected[row * size + column] = Pixel(plane, sourceX, sourceY, pattern);
                            }
                        }
                        Require(samples.SequenceEqual(expected));
                        for (int top = 0; top < size; top += 4)
                        {
                            for (int left = 0; left < size; left += 4)
                            {
                                VerifyPreparedTransform(samples, expected, size, top, left);
                            }
                        }
                    }
                    Require(source.Samples.SequenceEqual(prepared));
                    Require(guarded.AsSpan(0, 3).ContainsAnyExcept((byte)183) == false);
                    Require(guarded.AsSpan(3 + H264SourceMacroblock.ByteCount).ContainsAnyExcept((byte)183) == false);
                }
            }
        }

        private static void VerifyPreparedTransform(ReadOnlySpan<byte> samples, ReadOnlySpan<byte> expected, int stride, int top, int left)
        {
            foreach (int predictionStride in new[] { 4, 8, 16, 23 })
            {
                byte[] prediction = new byte[3 * predictionStride + 4];
                for (int index = 0; index < prediction.Length; index++)
                {
                    prediction[index] = (byte)((index * 197 + left * 59 + top * 131) & 255);
                }
                byte[] unchanged = (byte[])prediction.Clone();
                int[] actual = new int[18];
                actual.AsSpan().Fill(int.MinValue);
                H264EncodingBlocks.Transform(samples[(top * stride + left)..], stride, prediction, predictionStride, actual.AsSpan(1, 16));
                for (int vertical = 0; vertical < 4; vertical++)
                {
                    for (int horizontal = 0; horizontal < 4; horizontal++)
                    {
                        int coefficient = 0;
                        for (int row = 0; row < 4; row++)
                        {
                            for (int column = 0; column < 4; column++)
                            {
                                int residual = expected[(top + row) * stride + left + column] - prediction[row * predictionStride + column];
                                coefficient += Matrix[vertical * 4 + row] * residual * Matrix[horizontal * 4 + column];
                            }
                        }
                        Require(actual[1 + vertical * 4 + horizontal] == coefficient);
                    }
                }
                Require(actual[0] == int.MinValue && actual[^1] == int.MinValue);
                Require(prediction.AsSpan().SequenceEqual(unchanged));
            }
        }

        public static void VerifyAllocation()
        {
            MediaBufferAllocator allocator = new(64 * 1024);
            using (VideoFrame frame = Create(35, 33, true, 1, allocator))
            {
                Repeat(frame);
                long before = GC.GetAllocatedBytesForCurrentThread();
                Repeat(frame);
                Require(GC.GetAllocatedBytesForCurrentThread() == before);
            }
            Require(allocator.AllocatedBytes == 0);
        }

        private static void Repeat(VideoFrame frame)
        {
            Span<byte> samples = stackalloc byte[H264SourceMacroblock.ByteCount];
            Span<byte> prediction = stackalloc byte[16];
            prediction.Fill(128);
            Span<int> transformed = stackalloc int[16];
            for (int repeat = 0; repeat < 256; repeat++)
            {
                H264SourceMacroblock source = new(frame, 32, 32, samples);
                H264EncodingBlocks.Transform(source.Luma[(12 * 16 + 12)..], 16, prediction, 4, transformed);
                H264EncodingBlocks.Transform(source.Chroma[(4 * 8 + 4)..], 8, prediction, 4, transformed);
                H264EncodingBlocks.Transform(source.Chroma[(64 + 4 * 8 + 4)..], 8, prediction, 4, transformed);
            }
        }

        private static VideoFrame Create(int width, int height, bool padded, int pattern, MediaBufferAllocator allocator)
        {
            VideoFormat format = new(width, height, VideoPixelFormat.Yuv420P8);
            PlaneLayout[] planes = format.CreateTightLayout();
            if (padded)
            {
                int offset = 7;
                for (int plane = 0; plane < planes.Length; plane++)
                {
                    planes[plane] = new(offset, planes[plane].Stride + 13, planes[plane].RowBytes, planes[plane].Height);
                    offset = checked((int)planes[plane].EndOffset + 5);
                }
            }
            using WritableMediaBuffer writable = allocator.Allocate(checked((int)planes[^1].EndOffset));
            writable.Memory.Span.Fill(0xcd);
            for (int plane = 0; plane < planes.Length; plane++)
            {
                PlaneLayout layout = planes[plane];
                for (int row = 0; row < layout.Height; row++)
                {
                    for (int column = 0; column < layout.RowBytes; column++)
                    {
                        writable.Memory.Span[layout.Offset + row * layout.Stride + column] = Pixel(plane, column, row, pattern);
                    }
                }
            }
            using MediaBuffer buffer = writable.Publish();
            return new VideoFrame(format, buffer, planes);
        }

        private static byte Pixel(int plane, int x, int y, int pattern) => pattern switch
        {
            0 => 255,
            1 => (byte)((plane * 71 + x * 53 + y * 97 + x * y * 13) & 255),
            2 => (byte)((x + y + plane) % 2 * 255),
            _ => throw new ArgumentOutOfRangeException(nameof(pattern))
        };

        private static void Require(bool condition)
        {
            if (!condition)
            {
                throw new InvalidDataException("H.264 source blocks changed visible edge samples, transform coefficients or storage.");
            }
        }
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.Tests
{
    internal static class YuvResizeOwnershipTestInput
    {
        public static void Verify(ChromaLocation location, bool padded)
        {
            foreach (SampleAspectRatio? aspect in new SampleAspectRatio?[] { null, new(4, 3) })
            {
                MediaBufferAllocator source = new(4096);
                MediaBufferAllocator destination = new(4096);
                using VideoFrame input = CreateFrame(location, padded, aspect, source);
                ReadOnlyMemory<byte> memory = input.Data;
                using Yuv420AreaResizer resizer = Yuv420AreaResizer.CreateFit(input.Format, 8, 6, destination);
                using WritableMediaBuffer occupied = destination.Allocate((int)(destination.LimitBytes - destination.AllocatedBytes));
                using VideoFrame output = resizer.ResizeOrRetain(input);
                Require(!ReferenceEquals(input, output) && ReferenceEquals(input.Format, output.Format));
                Require(input.Data.Equals(output.Data) && input.Planes.SequenceEqual(output.Planes));
                Require(input.Timestamp == output.Timestamp && input.Duration == output.Duration);
                Require(source.AllocatedBytes == memory.Length && destination.AllocatedBytes == destination.LimitBytes);
                using VideoFrame retained = output.Retain();
                input.Dispose();
                output.Dispose();
                resizer.Dispose();
                occupied.Dispose();
                Require(destination.AllocatedBytes == 0 && source.AllocatedBytes == memory.Length);
                Require(memory.Equals(retained.Data));
                for (int index = 0; index < memory.Length; index++)
                {
                    Require(retained.Data.Span[index] == (byte)(index % 251));
                }
                retained.Dispose();
                Require(source.AllocatedBytes == 0);
            }
        }

        internal static VideoFrame CreateFrame(ChromaLocation location, bool padded, SampleAspectRatio? aspect,
            MediaBufferAllocator allocator)
        {
            VideoFormat format = new(8, 6, VideoPixelFormat.Yuv420P8,
                new(ColorPrimaries.Bt709, TransferFunction.Bt709, MatrixCoefficients.Bt709, ColorRange.Limited, location), aspect);
            PlaneLayout[] planes = format.CreateTightLayout();
            if (padded)
            {
                int offset = 7;
                for (int plane = 0; plane < planes.Length; plane++)
                {
                    planes[plane] = new(offset, planes[plane].Stride + 5, planes[plane].RowBytes, planes[plane].Height);
                    offset = checked((int)planes[plane].EndOffset + 3);
                }
            }
            using WritableMediaBuffer writable = allocator.Allocate(checked((int)planes[^1].EndOffset));
            for (int index = 0; index < writable.Memory.Length; index++)
            {
                writable.Memory.Span[index] = (byte)(index % 251);
            }
            using MediaBuffer buffer = writable.Publish();
            return new VideoFrame(format, buffer, planes, new(7, new(1, 24)), new(1, new(1, 24)));
        }

        private static void Require(bool condition)
        {
            if (!condition)
            {
                throw new InvalidDataException("YUV resize ownership, storage budget or frame identity changed.");
            }
        }
    }
}

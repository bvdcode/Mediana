// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.Tests
{
    internal static class YuvFramePreparationTestInput
    {
        public static void Verify()
        {
            foreach (int width in new[] { 1, 17, 31, 32, 33, 65, 1025 }) { VerifyPrecision(width); }
            MediaBufferAllocator allocator = new(4096);
            VideoFormat format = new(4, 2, VideoPixelFormat.Yuv420P8, new(ChromaLocation: ChromaLocation.Center), new(4, 3));
            using WritableMediaBuffer writable = allocator.Allocate(12);
            new byte[] { 10, 20, 30, 40, 50, 60, 70, 80, 100, 200, 50, 150 }.CopyTo(writable.Memory);
            using MediaBuffer buffer = writable.Publish();
            using VideoFrame input = new(format, buffer, format.CreateTightLayout());
            using VideoPreparation preparation = new(format, new(0, 1, 0, -1, 0, 0, 0, 0, 1), allocator);
            using VideoFrame output = preparation.Apply(input);
            if (output.Format.Width != 2 || output.Format.Height != 4 || output.Format.SampleAspectRatio != new SampleAspectRatio(3, 4) ||
                !output.Data.Span.SequenceEqual(new byte[] { 50, 10, 60, 20, 70, 30, 80, 40, 100, 200, 50, 150 }))
            {
                throw new InvalidDataException("Oriented YUV samples or pixel geometry differ from the reference.");
            }
            output.Dispose(); input.Dispose(); buffer.Dispose(); preparation.Dispose();
            if (allocator.AllocatedBytes != 0) { throw new InvalidDataException("YUV preparation retained pixel storage after disposal."); }
        }

        private static void VerifyPrecision(int width)
        {
            MediaBufferAllocator allocator = new(65536);
            VideoFormat format = new(width, 4, VideoPixelFormat.Yuv420P10);
            PlaneLayout[] planes = format.CreateTightLayout();
            using WritableMediaBuffer writable = allocator.Allocate((int)planes[^1].EndOffset);
            Span<ushort> values = MemoryMarshal.Cast<byte, ushort>(writable.Memory.Span);
            for (int index = 0; index < values.Length; index++) { values[index] = (ushort)(index % 1024); }
            using MediaBuffer buffer = writable.Publish();
            using VideoFrame input = new(format, buffer, planes);
            using VideoPreparation operation = new(format, VideoDisplayMatrix.Identity, allocator,
                new() { OutputPixelFormat = VideoPixelFormat.Yuv420P8 });
            using VideoFrame output = operation.Apply(input);
            for (int index = 0; index < values.Length; index++)
            {
                if (Math.Min(255, (values[index] + 2) / 4) != output.Data.Span[index])
                {
                    throw new InvalidDataException("YUV precision conversion differs from rounded scalar reference samples.");
                }
            }
            output.Dispose(); input.Dispose(); buffer.Dispose(); operation.Dispose();
            if (allocator.AllocatedBytes != 0) { throw new InvalidDataException("YUV precision conversion retained storage after disposal."); }
        }
    }
}

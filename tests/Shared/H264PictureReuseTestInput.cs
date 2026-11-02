// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.H264;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    internal static class H264PictureReuseTestInput
    {
        public static void Verify(bool intraOnly)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            VideoFormat format = new(320, 240, VideoPixelFormat.Yuv420P8,
                new(ColorPrimaries.Bt709, TransferFunction.Bt709, MatrixCoefficients.Bt709, ColorRange.Limited, ChromaLocation.Left), new(1, 1));
            PlaneLayout[] planes = format.CreateTightLayout();
            int bytes = planes.Sum(plane => checked(plane.Stride * plane.Height));
            using WritableMediaBuffer writable = allocator.Allocate(bytes);
            writable.Memory.Span.Fill(128);
            using MediaBuffer pixels = writable.Publish();
            using H264Encoder encoder = H264Encoder.Create(format, new(1, 25), allocator,
                new() { KeyFrameInterval = intraOnly ? TimeSpan.Zero : TimeSpan.FromSeconds(2) });
            MediaPacket? retained = null;
            byte[]? retainedBytes = null;
            long started = 0;
            try
            {
                for (int index = 0; index < 20; index++)
                {
                    if (index == 4)
                    {
                        started = GC.GetAllocatedBytesForCurrentThread();
                    }
                    using VideoFrame input = new(format, pixels, planes, new(index, new(1, 25)), new(1, new(1, 25)));
                    Require(encoder.TrySend(input) == CodecInputStatus.Accepted);
                    using MediaPacket packet = encoder.Receive().Value ?? throw new InvalidDataException("A reconstruction reuse run lost an encoded picture.");
                    Require(packet.PresentationTime == input.Timestamp && packet.IsKeyFrame == (intraOnly || index == 0));
                    if (retained is null)
                    {
                        retained = packet.Retain();
                        retainedBytes = retained.Data.ToArray();
                    }
                }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - started;
                Require(allocated < (long)bytes * 16);
                encoder.CompleteInput();
                Require(encoder.Receive().Status == CodecOutputStatus.EndOfStream);
                encoder.Dispose();
                Require(retained is not null && retained.Data.Span.SequenceEqual(retainedBytes)
                    && allocator.AllocatedBytes > bytes);
            }
            finally
            {
                retained?.Dispose();
            }
            pixels.Dispose();
            Require(allocator.AllocatedBytes == 0);
        }

        private static void Require(bool condition)
        {
            if (!condition)
            {
                throw new InvalidOperationException("H.264 reconstruction reuse changed amortized allocation, packet ownership or complete-input release.");
            }
        }
    }
}

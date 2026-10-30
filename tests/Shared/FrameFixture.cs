// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    internal static class FrameFixture
    {
        public static VideoFrame Create(MediaBufferAllocator allocator, byte value, long timestamp = 0)
        {
            VideoFormat format = new(1, 1, VideoPixelFormat.Rgba32);
            using WritableMediaBuffer writable = allocator.Allocate(4);
            writable.Memory.Span.Fill(value);
            using MediaBuffer buffer = writable.Publish();
            return new VideoFrame(format, buffer, format.CreateTightLayout(),
                new MediaTimestamp(timestamp, new TimeBase(1, 1)));
        }
    }
}

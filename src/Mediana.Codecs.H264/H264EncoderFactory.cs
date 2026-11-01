// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Codecs.H264
{
    public class H264EncoderFactory(H264EncoderOptions? options = null) : IVideoEncoderFactory
    {
        public string ImplementationId => "mediana.h264.encoder";
        public string CodecId => "h264";
        public IMediaEncoder<VideoFrame> Create(VideoFormat format, TimeBase timeBase, MediaBufferAllocator allocator, int streamIndex = 0) =>
            H264Encoder.Create(format, timeBase, allocator, options, streamIndex);
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Video;

namespace Mediana.Execution
{
    public class VideoDecodePipeline(IContainerReader reader, IMediaCodec<MediaPacket, VideoFrame> decoder,
        int streamIndex, long? maxPacketReads = null) : MediaDecodePipeline<VideoFrame>(reader, decoder, streamIndex, maxPacketReads)
    {
    }
}

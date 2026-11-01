// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Codecs
{
    public interface IVideoEncoderFactory : IEncoderFactory<VideoFrame, VideoFormat>
    {
    }
}

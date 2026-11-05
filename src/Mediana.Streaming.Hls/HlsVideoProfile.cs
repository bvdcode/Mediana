// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;

namespace Mediana.Streaming.Hls
{
    public record HlsVideoProfile(int MaximumWidth, H264EncoderOptions EncoderOptions);
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;

namespace Mediana.Cli
{
    internal record VideoConversionOptions(VideoCodecMode Codec, bool VideoOnly, int? Quantizer,
        int? Width, int? Height, int? BitRate, long? BufferMs)
    {
        public bool IsSpecified => Codec != VideoCodecMode.Copy || VideoOnly || Quantizer is not null ||
            Width is not null || Height is not null || BitRate is not null || BufferMs is not null;

        public VideoConversionRequest Create(string extension)
        {
            if (Codec != VideoCodecMode.H264 || extension != ".ts")
            {
                throw new NotSupportedException("H264 encoding requires .ts output and --video-codec H264; encoding options do not apply to stream copy or other outputs.");
            }
            if (Quantizer is < 0 or > 51) { throw new ArgumentOutOfRangeException(nameof(Quantizer)); }
            if (Width is < 2) { throw new ArgumentOutOfRangeException(nameof(Width), "Encoded video dimensions must be at least two."); }
            if (Height is < 2) { throw new ArgumentOutOfRangeException(nameof(Height), "Encoded video dimensions must be at least two."); }
            H264EncoderOptions options = new() { Quantizer = Quantizer ?? 26, RateControl = VideoRateOptions.Create(BitRate, BufferMs) };
            return new(options, Width, Height, VideoOnly);
        }
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using ConsoleAppFramework;
using Microsoft.Extensions.Logging;
using Mediana.Core.Metadata;

namespace Mediana.Cli
{
    internal class ConversionCommands(ILogger logger, TextWriter writer, CliOperation operation, CommandOutputPolicy outputs) : MediaCommands(outputs)
    {
        /// <summary>Inspect media: mediana -i input. Convert media: mediana -i input output.wav|output.mp3|output.png|output.jpg|output.webp|output.ts.</summary>
        /// <param name="output">Optional output file. Omit to inspect the input; its extension selects the conversion format.</param>
        /// <param name="input">-i, Existing input file; its content determines the input format.</param>
        /// <param name="stream">Audio or video stream index; defaults to the first stream of the required kind.</param>
        /// <param name="timeMs">Video/animation frame timestamp in milliseconds for image output.</param>
        /// <param name="page">One-based document page number for image output.</param>
        /// <param name="videoCodec">TS video mode: Copy preserves encoded streams; H264 encodes I/P video.</param>
        /// <param name="videoOnly">Explicitly omit audio when encoding H264 to TS.</param>
        /// <param name="qp">H264 quantizer from 0 to 51; fixed without video-bit-rate, otherwise the initial value. Defaults to 26.</param>
        /// <param name="width">Maximum encoded video storage width; rounded down to an even size without enlargement.</param>
        /// <param name="height">Maximum encoded video storage height; rounded down to an even size without enlargement.</param>
        /// <param name="videoBitRate">Constrained variable AVC packet rate in bits/second; omit for fixed QP.</param>
        /// <param name="videoBufferMs">Video packet buffer duration in milliseconds; requires video-bit-rate, defaults to 2000.</param>
        /// <param name="audioCodec">Select AAC-LC for .ts or Layer III for .mp3 audio output.</param>
        /// <param name="audioBitRate">Audio bitrate in bits/second; defaults to 128000.</param>
        /// <param name="audioChannels">Explicitly downmix encoded audio to 1 or 2 channels with normalized ITU coefficients; LFE is omitted.</param>
        /// <param name="imageCompression">Image compression: Lossless or Lossy; defaults to the output format preference.</param>
        /// <param name="quality">Lossy image quality from 0 to 100. Defaults to 75; WebP requires image-compression Lossy.</param>
        /// <param name="chromaSampling">JPEG sampling: Sampling420 (default), Sampling422, Sampling444 or Monochrome.</param>
        /// <param name="animate">Encode all animation or video frames to an animated image instead of one preview frame.</param>
        [Command("")]
        public Task<int> Convert(string input, [Argument] string? output = null, int? stream = null, long timeMs = 0, int page = 1,
            VideoCodecMode videoCodec = VideoCodecMode.Copy, bool videoOnly = false, int? qp = null,
            int? width = null, int? height = null, int? videoBitRate = null, long? videoBufferMs = null,
            AudioCodecMode? audioCodec = null, int? audioBitRate = null, int? audioChannels = null,
            ImageCompressionMode? imageCompression = null, int? quality = null, bool animate = false, VideoChromaSampling? chromaSampling = null,
            CancellationToken cancellationToken = default)
        {
            operation.Start(output is null ? "probe" : "conversion");
            ConversionSelection selection = new(
                new(videoCodec, videoOnly, qp, width, height, videoBitRate, videoBufferMs),
                new(audioCodec, audioBitRate, audioChannels),
                new(new(imageCompression, quality, chromaSampling), animate, timeMs, page));
            if (output is null)
            {
                if (stream is not null || selection.HasOptions)
                {
                    throw new ArgumentException("Conversion options require an output file. Use mediana -i input to inspect media.");
                }
                return ExecuteAsync(input, null, () => MediaProbeCommand.RunAsync(input, writer, cancellationToken));
            }
            if (stream is < 0) { throw new ArgumentOutOfRangeException(nameof(stream)); }
            ConversionRequest request = selection.Create(output);
            return ExecuteFileAsync(input, output,
                file => request.ExecuteAsync(input, file, stream, logger, writer, cancellationToken), cancellationToken);
        }
    }
}

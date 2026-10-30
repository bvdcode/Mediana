// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using ConsoleAppFramework;
using Microsoft.Extensions.Logging;

namespace Mediana.Cli
{
    internal class ConversionCommands(ILogger logger) : MediaCommands
    {
        /// <summary>Convert media: mediana -i input output.wav|output.png|output.ts.</summary>
        /// <param name="output">New output file. Its extension selects the output format.</param>
        /// <param name="input">-i, Existing input file; its content determines the input format.</param>
        /// <param name="stream">Audio or video stream index; defaults to the first stream of the required kind.</param>
        /// <param name="timeMs">Video/animation frame timestamp in milliseconds for PNG output.</param>
        /// <param name="page">One-based PDF page number for PNG output.</param>
        /// <param name="videoCodec">TS video mode: Copy preserves encoded streams; H264 encodes fixed-QP intra video.</param>
        /// <param name="videoOnly">Explicitly omit audio when encoding H264 to TS.</param>
        /// <param name="qp">Fixed H264 quantizer from 0 to 51; defaults to 26 when encoding.</param>
        /// <param name="width">Maximum encoded video storage width; rounded down to an even size without enlargement.</param>
        /// <param name="height">Maximum encoded video storage height; rounded down to an even size without enlargement.</param>
        [Command("")]
        public Task<int> Convert([Argument] string output, string input, int? stream = null, long timeMs = 0, int page = 1,
            VideoCodecMode videoCodec = VideoCodecMode.Copy, bool videoOnly = false, int? qp = null,
            int? width = null, int? height = null,
            CancellationToken cancellationToken = default)
        {
            if (stream is < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(stream));
            }
            ArgumentOutOfRangeException.ThrowIfNegative(timeMs);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(page);
            if (!Enum.IsDefined(videoCodec))
            {
                throw new ArgumentOutOfRangeException(nameof(videoCodec));
            }
            bool encode = videoCodec switch
            {
                VideoCodecMode.Copy => false,
                VideoCodecMode.H264 => true,
                _ => throw new ArgumentOutOfRangeException(nameof(videoCodec))
            };
            if (encode || videoOnly || qp is not null || width is not null || height is not null)
            {
                if (!encode || !videoOnly || !Path.GetExtension(output).Equals(".ts", StringComparison.OrdinalIgnoreCase))
                {
                    throw new NotSupportedException("H264 encoding requires .ts output, --video-codec H264 and --video-only; encoding options do not apply to stream copy or other outputs.");
                }
                if (qp is < 0 or > 51)
                {
                    throw new ArgumentOutOfRangeException(nameof(qp));
                }
                if (width is < 2)
                {
                    throw new ArgumentOutOfRangeException(nameof(width), "Encoded video dimensions must be at least two.");
                }
                if (height is < 2)
                {
                    throw new ArgumentOutOfRangeException(nameof(height), "Encoded video dimensions must be at least two.");
                }
                return ExecuteAsync(input, output, () => VideoTranscodeCommand.RunAsync(input, output, stream, qp ?? 26,
                    width, height, logger, cancellationToken));
            }
            return ExecuteAsync(input, output, () => Path.GetExtension(output).ToLowerInvariant() switch
            {
                ".wav" or ".wave" => WaveAudioCommand.RunAsync(input, output, stream, logger, cancellationToken),
                ".png" => FilePreviewCommand.RunAsync(input, output, stream, timeMs, page, cancellationToken),
                ".ts" => Mp4RemuxCommand.RunAsync(input, output, cancellationToken),
                _ => throw new NotSupportedException("Cannot select an output format from the extension '" + Path.GetExtension(output)
                    + "'. Use .wav or .wave (PCM16), .png (one image/frame/page), or .ts (AVC/AAC/MP3 stream copy).")
            });
        }
    }
}

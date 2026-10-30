// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using ConsoleAppFramework;
using Microsoft.Extensions.Logging;

namespace Mediana.Cli
{
    internal class ConversionCommands(ILogger logger, TextWriter writer, CliOperation operation, CommandOutputPolicy outputs) : MediaCommands(outputs)
    {
        /// <summary>Inspect media: mediana -i input. Convert media: mediana -i input output.wav|output.png|output.ts.</summary>
        /// <param name="output">Optional output file. Omit to inspect the input; its extension selects the conversion format.</param>
        /// <param name="input">-i, Existing input file; its content determines the input format.</param>
        /// <param name="stream">Audio or video stream index; defaults to the first stream of the required kind.</param>
        /// <param name="timeMs">Video/animation frame timestamp in milliseconds for PNG output.</param>
        /// <param name="page">One-based document page number for PNG output.</param>
        /// <param name="videoCodec">TS video mode: Copy preserves encoded streams; H264 encodes fixed-QP intra video.</param>
        /// <param name="videoOnly">Explicitly omit audio when encoding H264 to TS.</param>
        /// <param name="qp">Fixed H264 quantizer from 0 to 51; defaults to 26 when encoding.</param>
        /// <param name="width">Maximum encoded video storage width; rounded down to an even size without enlargement.</param>
        /// <param name="height">Maximum encoded video storage height; rounded down to an even size without enlargement.</param>
        /// <param name="audioCodec">Encode an audio-only TS file with AAC-LC.</param>
        /// <param name="audioBitRate">Raw AAC bitrate ceiling in bits/second; defaults to 128000.</param>
        [Command("")]
        public Task<int> Convert(string input, [Argument] string? output = null, int? stream = null, long timeMs = 0, int page = 1,
            VideoCodecMode videoCodec = VideoCodecMode.Copy, bool videoOnly = false, int? qp = null,
            int? width = null, int? height = null, AudioCodecMode? audioCodec = null, int? audioBitRate = null,
            CancellationToken cancellationToken = default)
        {
            operation.Start(output is null ? "probe" : "conversion");
            if (output is null)
            {
                if (stream is not null || timeMs != 0 || page != 1 || videoCodec != VideoCodecMode.Copy || videoOnly ||
                    qp is not null || width is not null || height is not null || audioCodec is not null || audioBitRate is not null)
                {
                    throw new ArgumentException("Conversion options require an output file. Use mediana -i input to inspect media.");
                }
                return ExecuteAsync(input, null, () => MediaProbeCommand.RunAsync(input, writer, cancellationToken));
            }
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
            if (audioCodec is not null || audioBitRate is not null)
            {
                if (audioCodec is not AudioCodecMode.Aac || !Path.GetExtension(output).Equals(".ts", StringComparison.OrdinalIgnoreCase) ||
                    encode || videoOnly || qp is not null || width is not null || height is not null)
                {
                    throw new NotSupportedException("AAC encoding requires .ts output and --audio-codec Aac; video encoding options cannot be combined with audio-only output.");
                }
                return ExecuteFileAsync(input, output, outputFile => AudioEncodeCommand.RunAsync(input, outputFile, stream, audioBitRate ?? 128000, logger, cancellationToken), cancellationToken);
            }
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
                return ExecuteFileAsync(input, output, outputFile => VideoTranscodeCommand.RunAsync(input, outputFile, stream, qp ?? 26,
                    width, height, logger, cancellationToken), cancellationToken);
            }
            return ExecuteFileAsync(input, output, outputFile => Path.GetExtension(output).ToLowerInvariant() switch
            {
                ".wav" or ".wave" => WaveAudioCommand.RunAsync(input, outputFile, stream, logger, cancellationToken),
                ".png" => FilePreviewCommand.RunAsync(input, outputFile, stream, timeMs, page, cancellationToken),
                ".ts" => MpegTsRemuxCommand.RunAsync(input, outputFile, stream, cancellationToken),
                _ => throw new NotSupportedException("Cannot select an output format from the extension '" + Path.GetExtension(output)
                    + "'. Use .wav or .wave (PCM16), .png (one image/frame/page), or .ts (AVC/AAC/MP3 stream copy).")
            }, cancellationToken);
        }
    }
}

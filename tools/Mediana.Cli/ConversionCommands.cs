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
        [Command("")]
        public Task<int> Convert([Argument] string output, string input, int? stream = null, long timeMs = 0, int page = 1,
            CancellationToken cancellationToken = default)
        {
            if (stream is < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(stream));
            }
            ArgumentOutOfRangeException.ThrowIfNegative(timeMs);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(page);
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

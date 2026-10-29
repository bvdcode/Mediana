// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using ConsoleAppFramework;

namespace Mediana.Cli
{
    internal class VideoCommands : MediaCommands
    {
        /// <summary>Extract a supported H.264 frame to an sRGB PNG.</summary>
        /// <param name="input">Existing MP4/MOV file.</param>
        /// <param name="output">New PNG file.</param>
        /// <param name="stream">Video stream index.</param>
        /// <param name="timeMs">Select the first frame at or after this media timestamp in milliseconds.</param>
        /// <param name="width">Maximum preview width.</param>
        /// <param name="height">Maximum preview height.</param>
        public Task<int> Preview([Argument] string input, [Argument] string output, int stream = 0, long timeMs = 0,
            int width = 320, int height = 180, CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(stream);
            ArgumentOutOfRangeException.ThrowIfNegative(timeMs);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            return ExecuteAsync(input, output, () => VideoPreviewCommand.RunAsync(input, output, stream, timeMs, width, height, cancellationToken));
        }
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using ConsoleAppFramework;

namespace Mediana.Cli
{
    internal class HlsCommands : MediaCommands
    {
        /// <summary>List HLS segment boundaries for an MP4/MOV video track.</summary>
        /// <param name="input">Existing MP4/MOV file.</param>
        /// <param name="videoStream">Video stream index.</param>
        /// <param name="segmentMs">Target segment duration in milliseconds.</param>
        public Task<int> Plan([Argument] string input, int videoStream = 0, long segmentMs = 6000,
            CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(videoStream);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(segmentMs);
            return ExecuteAsync(input, null, () => HlsPlanCommand.RunAsync(input, videoStream, segmentMs, cancellationToken));
        }

        /// <summary>Write a VOD playlist and MPEG-TS segments without re-encoding.</summary>
        /// <param name="input">Existing MP4/MOV file.</param>
        /// <param name="output">New output directory.</param>
        /// <param name="videoStream">Video stream index.</param>
        /// <param name="audioStream">Optional audio stream index; omit for video-only HLS.</param>
        /// <param name="segmentMs">Target segment duration in milliseconds.</param>
        public Task<int> Create([Argument] string input, [Argument] string output, int videoStream = 0, int? audioStream = null,
            long segmentMs = 6000, CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(videoStream);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(segmentMs);
            if (audioStream is < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(audioStream));
            }
            return ExecuteAsync(input, output, () => HlsCopyCommand.RunAsync(input, output, videoStream, audioStream, segmentMs, cancellationToken));
        }
    }
}

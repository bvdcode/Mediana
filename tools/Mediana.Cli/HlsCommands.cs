// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using ConsoleAppFramework;
using Microsoft.Extensions.Logging;

namespace Mediana.Cli
{
    internal class HlsCommands(ILogger logger) : MediaCommands
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

        /// <summary>Encode aligned video-only H264 variants and write a complete VOD master playlist.</summary>
        /// <param name="input">Existing supported MP4/MOV file.</param>
        /// <param name="output">New output directory for all variants.</param>
        /// <param name="videoOnly">Explicitly omit audio from every variant.</param>
        /// <param name="videoStream">Video stream index.</param>
        /// <param name="segmentMs">Target segment duration in milliseconds.</param>
        /// <param name="qp">Fixed H264 quantizer from 0 to 51.</param>
        /// <param name="widths">Maximum storage widths, separated by spaces; no enlargement. Put this option last.</param>
        public Task<int> Encode([Argument] string input, [Argument] string output, bool videoOnly = false,
            int videoStream = 0, long segmentMs = 6000, int qp = 26, CancellationToken cancellationToken = default, params int[] widths)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(videoStream);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(segmentMs);
            ArgumentOutOfRangeException.ThrowIfNegative(qp);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(qp, 51);
            if (!videoOnly)
            {
                throw new NotSupportedException("HLS encoding currently requires --video-only to explicitly omit audio.");
            }
            if (widths.Length == 0 || widths.Any(width => width < 2) || widths.Distinct().Count() != widths.Length)
            {
                throw new ArgumentException("Supply unique --widths values of at least two, separated by spaces.", nameof(widths));
            }
            return ExecuteAsync(input, output, () => HlsEncodeCommand.RunAsync(input, output, videoStream, widths, qp, segmentMs, logger, cancellationToken));
        }
    }
}

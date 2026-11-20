// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Processing
{
    /// <summary>Configured spatial field interpolation. Produces one progressive frame per input at unchanged timing.
    /// Borrows the allocator and input; retains no pixel storage between calls.</summary>
    public class VideoFieldPreparation
    {
        private readonly VideoFormat inputFormat;
        private readonly MediaBufferAllocator allocator;
        private readonly VideoFieldParity? parity;
        private readonly int offset;

        public VideoFieldPreparation(VideoFormat inputFormat, VideoFieldSelection selection, MediaBufferAllocator allocator,
            Action<string>? warningHandler = null)
        {
            ArgumentNullException.ThrowIfNull(inputFormat);
            ArgumentNullException.ThrowIfNull(allocator);
            if (!Enum.IsDefined(selection)) { throw new ArgumentOutOfRangeException(nameof(selection)); }
            this.inputFormat = inputFormat;
            this.allocator = allocator;
            VideoFieldParity? leading = inputFormat.ScanMode switch
            {
                VideoScanMode.Progressive => null,
                VideoScanMode.TopFieldFirst or VideoScanMode.TopField => VideoFieldParity.Top,
                VideoScanMode.BottomFieldFirst or VideoScanMode.BottomField => VideoFieldParity.Bottom,
                _ => throw new ArgumentOutOfRangeException(nameof(inputFormat))
            };
            if (leading is null)
            {
                OutputFormat = inputFormat;
                return;
            }
            parity = selection switch
            {
                VideoFieldSelection.Leading => leading.Value,
                VideoFieldSelection.Top => VideoFieldParity.Top,
                VideoFieldSelection.Bottom => VideoFieldParity.Bottom,
                _ => throw new ArgumentOutOfRangeException(nameof(selection))
            };
            VideoFormat field = inputFormat.GetFieldFormat(parity.Value);
            int height = field.FullFrameHeight
                ?? throw new NotSupportedException("Field interpolation requires a declared full frame height.");
            (OutputFormat, offset) = VideoFieldInterpolator.Describe(field, height);
            warningHandler?.Invoke($"Spatial interpolation of the {parity.Value.ToString().ToLowerInvariant()} field converts interlaced input to progressive output at the source frame cadence.");
        }

        public VideoFormat OutputFormat { get; }
        public bool RequiresInterpolation => parity is not null;

        public VideoFrame Apply(VideoFrame input, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            cancellationToken.ThrowIfCancellationRequested();
            VideoFormat actual = input.Format;
            if (!VideoFormatComparison.GeometryMatches(actual, inputFormat) || actual.Color != inputFormat.Color ||
                !Equals(actual.ColorDescription, inputFormat.ColorDescription))
            {
                throw new ArgumentException("The input differs from the configured field preparation format.", nameof(input));
            }
            if (parity is not VideoFieldParity selected)
            {
                return input.Retain();
            }
            using VideoFrame field = input.GetField(selected);
            return VideoFieldInterpolator.Apply(field, OutputFormat, offset, allocator, cancellationToken);
        }
    }
}

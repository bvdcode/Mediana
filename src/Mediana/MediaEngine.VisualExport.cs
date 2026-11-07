// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using Mediana.Codecs;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Execution;
using Mediana.IO;
using Mediana.Processing;

namespace Mediana
{
    public partial class MediaEngine
    {
        /// <summary>Exports one image, document page, animation frame or selected video frame as sRGB.
        /// Borrows source, destination, encoder and allocator. Failed output must be discarded.</summary>
        public async Task<VisualExportResult> ExportPreviewAsync(IByteSource source, Stream destination, IImageEncoder encoder,
            MediaBufferAllocator allocator, VisualExportOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(encoder);
            ValidateDestination(destination);
            options ??= new(); options.Validate();
            VisualOpenOptions input = options.Input with
            {
                ReadImageSequence = false,
                Request = options.Input.Request with { WarningHandler = options.Input.Request.WarningHandler + options.WarningHandler }
            };
            await using VisualSession session = await OpenVisualAsync(source, allocator, input, cancellationToken).ConfigureAwait(false);
            using VideoFrame frame = await session.ReadFrameAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("There is no frame at or after the requested timestamp.");
            bool preserveAspect = session.IsImage && (encoder.Features & ImageEncoderFeatures.SampleAspectRatio) != 0;
            return await ExportImageAsync(frame, destination, encoder, allocator, session.DisplayMatrix, preserveAspect, options, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Exports borrowed pixels through the same presentation policy as visual previews.</summary>
        public static async Task<VisualExportResult> ExportImageAsync(VideoFrame frame, Stream destination, IImageEncoder encoder,
            MediaBufferAllocator allocator, VideoDisplayMatrix displayMatrix, bool preserveSampleAspectRatio = false,
            VisualExportOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(frame);
            ArgumentNullException.ThrowIfNull(encoder);
            ValidateDestination(destination);
            options ??= new(); options.Validate();
            using VideoFrame output = VisualFramePreparation.Apply(frame, displayMatrix, preserveSampleAspectRatio, options, allocator, cancellationToken);
            long bytes = await encoder.EncodeAsync(output, destination, allocator, cancellationToken).ConfigureAwait(false);
            return new(1, bytes, output.Format.Width, output.Format.Height, output.Timestamp, output.Duration);
        }

        /// <summary>Exports an image animation or video without decoding it twice. Frame durations are quantized cumulatively.
        /// Borrows source, destination, encoder and allocator. Reports each distinct warning once per operation.</summary>
        public async Task<VisualExportResult> ExportAnimationAsync(IByteSource source, Stream destination, IImageSequenceEncoder encoder,
            MediaBufferAllocator allocator, VisualExportOptions? options = null, Action<MediaOutputProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(encoder);
            ValidateDestination(destination);
            options ??= new(); options.Validate();
            HashSet<string> warnings = [];
            Action<string>? handler = options.WarningHandler;
            void Warn(string message) { if (warnings.Add(message)) { handler?.Invoke(message); } }
            options = options with { WarningHandler = Warn };
            VisualOpenOptions input = options.Input with
            {
                ReadImageSequence = true,
                Request = options.Input.Request with { WarningHandler = options.Input.Request.WarningHandler + Warn }
            };
            await using VisualSession session = await OpenVisualAsync(source, allocator, input, cancellationToken).ConfigureAwait(false);
            VideoFrameTimeQuantizer clock = new(encoder.DurationTimeBase);
            long count = 0, ticks = 0;
            int width = 0, height = 0;
            MediaTimestamp? first = null;
            using CountingWriteStream output = new(destination);
            long bytes = await encoder.EncodeAsync(PrepareAsync(cancellationToken), output, allocator, cancellationToken).ConfigureAwait(false);
            MediaDuration duration = new(ticks, encoder.DurationTimeBase);
            progress?.Invoke(new(duration, bytes, true, session.CodedDuration));
            return new(count, bytes, width, height, first, duration);

            async IAsyncEnumerable<VideoFrame> PrepareAsync([EnumeratorCancellation] CancellationToken token)
            {
                while (await session.ReadFrameAsync(token).ConfigureAwait(false) is VideoFrame frame)
                {
                    using (frame)
                    using (VideoFrame pixels = VisualFramePreparation.Apply(frame, session.DisplayMatrix, false, options, allocator, token))
                    using (VideoFrame timed = clock.Apply(pixels))
                    {
                        if (count == 0) { first = timed.Timestamp; width = timed.Format.Width; height = timed.Format.Height; }
                        yield return timed;
                        count = checked(count + 1);
                        ticks = checked(ticks + timed.Duration!.Value.Value);
                        progress?.Invoke(new(new(ticks, encoder.DurationTimeBase), output.BytesWritten, false, session.CodedDuration));
                    }
                }
            }
        }

        private static void ValidateDestination(Stream destination)
        {
            ArgumentNullException.ThrowIfNull(destination);
            if (!destination.CanWrite) { throw new ArgumentException("The output destination must be writable.", nameof(destination)); }
        }
    }
}

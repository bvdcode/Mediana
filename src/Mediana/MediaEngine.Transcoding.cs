// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Execution;
using Mediana.IO;
using Mediana.Processing;
using Mediana.Processing.Audio;

namespace Mediana
{
    public partial class MediaEngine
    {
        /// <summary>Decodes, optionally resizes and encodes one video stream using registered codec and muxer factories.
        /// Borrows source, destination and allocator. Input allocations share its budget and retain their configured local limit.
        /// Applies declared display orientation and configured planar preparation without discarding audio implicitly.
        /// Failed output must be discarded.</summary>
        public async Task<MediaOperationProgress> TranscodeVideoAsync(IByteSource source, Stream destination,
            string codecId, string containerFormatId, MediaBufferAllocator allocator, VideoTranscodeOptions? options = null,
            Action<MediaOutputProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            ValidateDestination(destination);
            ArgumentNullException.ThrowIfNull(allocator);
            options ??= new();
            if (options.MaximumWidth is < 2 || options.MaximumHeight is < 2) { throw new ArgumentOutOfRangeException(nameof(options)); }
            await using ContainerSession container = await OpenContainerAsync(source, allocator, options.Input, cancellationToken).ConfigureAwait(false);
            if (!options.OmitAudio && container.Streams.Any(stream => stream.Kind == MediaKind.Audio))
            { throw new NotSupportedException("The input contains audio. Video-only encoding requires explicitly omitting audio tracks."); }
            await using VideoSession session = OpenVideo(container, options.StreamIndex, cancellationToken);
            VideoDisplayMatrix matrix = session.Stream.DisplayMatrix
                ?? throw new NotSupportedException("Video encoding requires a known source display transform.");
            using VideoFrame first = await session.ReadFrameAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("The input contains no decoded video frames.");
            using VideoPreparation preparation = new(first.Format, matrix, allocator, options);
            using IMediaEncoder<VideoFrame> encoder = CreateVideoEncoder(codecId, preparation.OutputFormat, session.Stream.TimeBase, allocator);
            using CountingWriteStream output = new(destination);
            await using IContainerWriter writer = CreateContainerWriter(containerFormatId, output, [encoder.Stream], allocator);
            bool opening = true;
            return await MediaOperations.EncodeVideoAsync(ReadAsync, encoder, writer,
                update => progress?.Invoke(new(update.ProcessedDuration, output.BytesWritten, update.IsComplete, session.Stream.CodedDuration)),
                cancellationToken).ConfigureAwait(false);

            async ValueTask<VideoFrame?> ReadAsync(CancellationToken token)
            {
                using VideoFrame? frame = opening ? first.Retain() : await session.ReadFrameAsync(token).ConfigureAwait(false);
                if (opening) { opening = false; first.Dispose(); }
                return frame is null ? null : preparation.Apply(frame, token);
            }
        }

        /// <summary>Decodes and prepares mono/stereo Float32 PCM for a registered encoder and muxer.
        /// Borrows source, destination and allocator; explicitly selected output channels use normalized downmix coefficients.</summary>
        public async Task<AudioTranscodeResult> TranscodeAudioAsync(IByteSource source, Stream destination,
            string codecId, string containerFormatId, MediaBufferAllocator allocator, AudioTranscodeOptions? options = null,
            Action<MediaOutputProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            ValidateDestination(destination);
            ArgumentNullException.ThrowIfNull(allocator);
            options ??= new();
            await using AudioSession session = await OpenAudioAsync(source, allocator, options.Input, options.StreamIndex, cancellationToken).ConfigureAwait(false);
            using AudioFrame first = await session.ReadFrameAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("The input contains no decoded audio samples.");
            AudioEncodingPcm converter = new(first.Format, options.Channels);
            using AudioFrame prepared = converter.Apply(first, allocator, cancellationToken);
            using IMediaEncoder<AudioFrame> encoder = CreateAudioEncoder(codecId, prepared.Format, new(1, prepared.Format.SampleRate), allocator);
            using CountingWriteStream output = new(destination);
            await using IContainerWriter writer = CreateContainerWriter(containerFormatId, output, [encoder.Stream], allocator);
            bool opening = true;
            long samples = 0;
            MediaOperationProgress result = await MediaOperations.EncodeAudioAsync(ReadAsync, encoder, writer,
                update => progress?.Invoke(new(update.ProcessedDuration, output.BytesWritten, update.IsComplete, session.Stream.CodedDuration)),
                cancellationToken).ConfigureAwait(false);
            IAudioEncoderTiming? timing = encoder as IAudioEncoderTiming;
            return new(samples, result, timing?.PrimingSamples, timing?.PaddingSamples);

            async ValueTask<AudioFrame?> ReadAsync(CancellationToken token)
            {
                AudioFrame? result;
                if (opening)
                {
                    opening = false; result = prepared.Retain(); prepared.Dispose(); first.Dispose();
                }
                else
                {
                    using AudioFrame? frame = await session.ReadFrameAsync(token).ConfigureAwait(false);
                    result = frame is null ? null : converter.Apply(frame, allocator, token);
                }
                if (result is not null) { samples = checked(samples + result.SamplesPerChannel); }
                return result;
            }
        }

        /// <summary>Copies selected compressed streams using registered container factories. Null selection chooses audio and video.
        /// Borrows source, destination and output allocator. Writers validate codec compatibility before packet reading.</summary>
        public async Task<MediaOperationProgress> RemuxAsync(IByteSource source, Stream destination, string containerFormatId,
            MediaBufferAllocator allocator, IReadOnlyCollection<int>? streamIndices = null, MediaOpenOptions? options = null,
            Action<MediaOutputProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            ValidateDestination(destination);
            ArgumentNullException.ThrowIfNull(allocator);
            await using ContainerSession reader = await OpenContainerAsync(source, allocator, options ?? new(), cancellationToken).ConfigureAwait(false);
            if (streamIndices is not null && (streamIndices.Count == 0 || streamIndices.Distinct().Count() != streamIndices.Count))
            { throw new ArgumentException("Select unique existing streams.", nameof(streamIndices)); }
            EncodedStreamInfo[] selected = streamIndices is null
                ? reader.Streams.Where(stream => stream.Kind is MediaKind.Audio or MediaKind.Video).ToArray()
                : streamIndices.Select(index => reader.Streams.SingleOrDefault(stream => stream.StreamIndex == index)
                    ?? throw new ArgumentException("The requested stream does not exist.", nameof(streamIndices))).ToArray();
            if (selected.Length == 0) { throw new NotSupportedException("The input contains no selected audio or video streams."); }
            using CountingWriteStream output = new(destination);
            await using IContainerWriter writer = CreateContainerWriter(containerFormatId, output, selected, allocator);
            return await MediaOperations.CopyAsync(reader, writer, selected.Select(stream => stream.StreamIndex).ToArray(),
                update => progress?.Invoke(new(update.ProcessedDuration, output.BytesWritten, update.IsComplete, reader.Info.DeclaredDuration)),
                cancellationToken).ConfigureAwait(false);
        }
    }
}

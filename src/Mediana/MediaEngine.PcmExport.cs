// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Execution;
using Mediana.IO;
using Mediana.Processing.Audio;

namespace Mediana
{
    public partial class MediaEngine
    {
        /// <summary>Streams decoded audio into a supplied PCM container writer, converting the sample representation once.
        /// Preserves sample rate and channels. Borrows source, destination, writer factory and output allocator.
        /// Input storage shares the allocator and retains its configured local limit. Failed output must be discarded.</summary>
        public async Task<PcmExportResult> ExportPcmAsync(IByteSource source, Stream destination,
            IAudioFrameWriterFactory factory, MediaBufferAllocator allocator, PcmExportOptions? options = null,
            Action<MediaOutputProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            ValidateDestination(destination);
            ArgumentNullException.ThrowIfNull(factory);
            ArgumentNullException.ThrowIfNull(allocator);
            options ??= new();
            await using AudioSession session = await OpenAudioAsync(source, allocator, options.Input, options.StreamIndex, cancellationToken).ConfigureAwait(false);
            using AudioFrame first = await session.ReadFrameAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("The input contains no decoded audio samples.");
            AudioFormat format = new(first.Format.SampleRate, options.SampleFormat, AudioStorageLayout.Interleaved, first.Format.Channels.ToArray());
            using CountingWriteStream output = new(destination);
            await using IAudioFrameWriter writer = await factory.CreateAsync(output, format, allocator, cancellationToken).ConfigureAwait(false);
            long samples = 0;
            await WriteAsync(first).ConfigureAwait(false);
            first.Dispose();
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using AudioFrame? frame = await session.ReadFrameAsync(cancellationToken).ConfigureAwait(false);
                if (frame is null) { break; }
                await WriteAsync(frame).ConfigureAwait(false);
            }
            await writer.CompleteAsync(cancellationToken).ConfigureAwait(false);
            progress?.Invoke(new(new(samples, new TimeBase(1, format.SampleRate)), output.BytesWritten, true, session.Stream.CodedDuration));
            return new(samples, format, output.BytesWritten);

            async ValueTask WriteAsync(AudioFrame frame)
            {
                using AudioFrame pcm = AudioSampleConverter.ConvertOrRetain(frame, options.SampleFormat, AudioStorageLayout.Interleaved, allocator, cancellationToken);
                await writer.WriteFrameAsync(pcm, cancellationToken).ConfigureAwait(false);
                samples = checked(samples + pcm.SamplesPerChannel);
                progress?.Invoke(new(new(samples, new TimeBase(1, format.SampleRate)), output.BytesWritten, false, session.Stream.CodedDuration));
            }
        }
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Jpeg;
using Mediana.Codecs.Png;
using Mediana.Containers.Matroska;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.IO;

namespace Mediana.Sample
{
    internal static class AudioPreviewSample
    {
        private const long BufferBudgetBytes = 64 * 1024 * 1024;

        internal static MediaEngine CreateEngine() => AudioWaveformSample.CreateBuilder()
            .AddContainer(new MatroskaReaderFactory())
            .AddImageDecoder(new PngImageDecoder()).AddImageDecoder(new JpegImageDecoder()).Build();

        internal static async Task<int> RunAsync(string input, string coverOutput, string waveformOutput,
            CancellationToken cancellationToken = default)
        {
            await using RandomAccessStreamByteSource source = new(File.OpenRead(input));
            AudioPreviewKind kind = await WriteAsync(CreateEngine(), source, coverOutput, waveformOutput, cancellationToken);
            await Console.Out.WriteLineAsync($"Audio preview: {kind}.");
            return 0;
        }

        internal static async Task<AudioPreviewKind> WriteAsync(MediaEngine engine, IByteSource source,
            string coverOutput, string waveformOutput, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(coverOutput);
            ArgumentException.ThrowIfNullOrWhiteSpace(waveformOutput);
            await using ContainerSession container = await engine.OpenContainerAsync(source,
                new MediaOpenOptions { BufferBudgetBytes = BufferBudgetBytes }, cancellationToken);
            using ContainerImage? image = await engine.ReadContainerImageAsync(container,
                new([ContainerImageSource.Picture, ContainerImageSource.Attachment]), cancellationToken);
            if (image is not null)
            {
                await container.DisposeAsync();
                MediaBufferAllocator allocator = new(BufferBudgetBytes);
                {
                    using VideoFrame decoded = await engine.DecodeImageAsync(image.Data, allocator, cancellationToken: cancellationToken);
                    using VideoFrame preview = await VideoPreviewSample.FitAsync(decoded, VideoDisplayMatrix.Identity, allocator, cancellationToken);
                    await using FileStream destination = new(coverOutput, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    await PngEncoder.EncodeAsync(preview, destination, allocator, cancellationToken);
                }
                if (allocator.AllocatedBytes != 0)
                {
                    throw new InvalidOperationException("The audio cover retained image buffers.");
                }
                return AudioPreviewKind.Cover;
            }
            await using AudioSession audio = engine.OpenAudio(container, cancellationToken: cancellationToken);
            await using (FileStream destination = new(waveformOutput, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await AudioWaveformSample.WriteAsync(audio, destination, cancellationToken);
            }
            if (container.AllocatedBufferBytes != 0)
            {
                throw new InvalidOperationException("The audio preview retained container buffers.");
            }
            return AudioPreviewKind.Waveform;
        }
    }
}

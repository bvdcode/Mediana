// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Core.Audio;
using Mediana.Execution;
using Mediana.IO;

namespace Mediana
{
    /// <summary>Immutable module registry. Factories must be safe for concurrent creation of independent sessions.</summary>
    public partial class MediaEngine
    {
        private readonly IContainerReaderFactory[] containers;
        private readonly IVideoDecoderFactory[] decoders;
        private readonly IAudioDecoderFactory[] audioDecoders;
        private readonly Dictionary<string, IStreamMetadataReader> metadataReaders;

        internal MediaEngine(IContainerReaderFactory[] containers, IVideoDecoderFactory[] decoders, IAudioDecoderFactory[] audioDecoders,
            IStreamMetadataReader[] metadataReaders)
        {
            this.containers = containers;
            this.decoders = decoders;
            this.audioDecoders = audioDecoders;
            this.metadataReaders = metadataReaders.ToDictionary(item => item.CodecId, StringComparer.Ordinal);
            StreamMetadataReaderImplementations = Array.AsReadOnly(metadataReaders.Select(item => item.ImplementationId).ToArray());
            AudioDecoderImplementations = Array.AsReadOnly(audioDecoders.Select(item => item.ImplementationId).ToArray());
            ContainerImplementations = Array.AsReadOnly(containers.Select(item => item.ImplementationId).ToArray());
            VideoDecoderImplementations = Array.AsReadOnly(decoders.Select(item => item.ImplementationId).ToArray());
        }

        public IReadOnlyList<string> StreamMetadataReaderImplementations { get; }
        public IReadOnlyList<string> ContainerImplementations { get; }
        public IReadOnlyList<string> VideoDecoderImplementations { get; }
        public IReadOnlyList<string> AudioDecoderImplementations { get; }

        /// <summary>Opens the explicitly selected stream. Borrows the byte source until the returned session is disposed.
        /// Sequential sources are consumed even on failure. Random access sources open at their origin
        /// without changing the caller's sequential position. No temporary file is used.</summary>
        public async ValueTask<VideoSession> OpenVideoAsync(IByteSource source, int streamIndex,
            MediaOpenOptions? options = null, CancellationToken cancellationToken = default) =>
            new(await OpenCoreAsync(source, streamIndex, MediaKind.Video, decoders, options, cancellationToken).ConfigureAwait(false));

        /// <summary>Opens the first video stream in container order. Borrows the source until session disposal.</summary>
        public async ValueTask<VideoSession> OpenVideoAsync(IByteSource source,
            MediaOpenOptions? options = null, CancellationToken cancellationToken = default) =>
            new(await OpenCoreAsync(source, null, MediaKind.Video, decoders, options, cancellationToken).ConfigureAwait(false));

        /// <summary>Opens the selected audio stream. Borrows the source; frames belong to the caller.
        /// Sequential sources are consumed on failure. Random access opening preserves the caller's cursor.</summary>
        public async ValueTask<AudioSession> OpenAudioAsync(IByteSource source, int streamIndex,
            MediaOpenOptions? options = null, CancellationToken cancellationToken = default) =>
            new(await OpenCoreAsync(source, streamIndex, MediaKind.Audio, audioDecoders, options, cancellationToken).ConfigureAwait(false));

        /// <summary>Opens the first audio stream in container order. Borrows the source; returned frames belong to the caller.</summary>
        public async ValueTask<AudioSession> OpenAudioAsync(IByteSource source,
            MediaOpenOptions? options = null, CancellationToken cancellationToken = default) =>
            new(await OpenCoreAsync(source, null, MediaKind.Audio, audioDecoders, options, cancellationToken).ConfigureAwait(false));

        private async ValueTask<MediaDecodeSession<TFrame>> OpenCoreAsync<TFrame>(IByteSource source, int? streamIndex,
            MediaKind kind, IReadOnlyList<IDecoderFactory<TFrame>> factories, MediaOpenOptions? options, CancellationToken cancellationToken)
            where TFrame : class, IDisposable
        {
            if (streamIndex is < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(streamIndex));
            }
            options ??= new MediaOpenOptions();
            ContainerSession container = await OpenContainerAsync(source, options, cancellationToken).ConfigureAwait(false);
            IContainerReader reader = container.Reader;
            MediaBufferAllocator allocator = container.Allocator;
            IMediaCodec<MediaPacket, TFrame>? decoder = null;
            MediaDecodePipeline<TFrame>? pipeline = null;
            bool transferred = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                EncodedStreamInfo stream;
                if (streamIndex is int selectedIndex)
                {
                    stream = reader.Streams.SingleOrDefault(item => item.StreamIndex == selectedIndex)
                        ?? throw new ArgumentException("The requested stream does not exist.", nameof(streamIndex));
                }
                else
                {
                    stream = reader.Streams.FirstOrDefault(item => item.Kind == kind)
                        ?? throw new NotSupportedException("The input contains no stream of the requested media kind.");
                }
                if (stream.Kind != kind)
                {
                    throw new NotSupportedException("The requested stream has a different media kind.");
                }
                IDecoderFactory<TFrame> decoderFactory = SelectDecoder(stream, factories);
                decoder = decoderFactory.Create(stream, allocator);
                IRandomAccessInspector? inspector = decoderFactory is IRandomAccessDecoderFactory randomAccessFactory
                    ? randomAccessFactory.CreateRandomAccessInspector(stream) : null;
                pipeline = new MediaDecodePipeline<TFrame>(reader, decoder, stream.StreamIndex, options.MaxPacketReads, inspector);
                MediaDecodeSession<TFrame> session = new(container, pipeline, options.MaxFrameReads, stream);
                cancellationToken.ThrowIfCancellationRequested();
                transferred = true;
                return session;
            }
            finally
            {
                if (!transferred)
                {
                    try
                    {
                        if (pipeline is not null)
                        {
                            pipeline.Dispose();
                        }
                        else
                        {
                            decoder?.Dispose();
                        }
                    }
                    finally
                    {
                        await container.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
        }

        private static IDecoderFactory<TFrame> SelectDecoder<TFrame>(EncodedStreamInfo stream, IReadOnlyList<IDecoderFactory<TFrame>> factories)
            where TFrame : class, IDisposable
        {
            IDecoderFactory<TFrame>? selected = null;
            List<string> reasons = [];
            foreach (IDecoderFactory<TFrame> factory in factories)
            {
                if (factory.CodecId != stream.CodecId)
                {
                    continue;
                }
                string? reason = factory.GetUnsupportedReason(stream);
                if (reason is not null)
                {
                    reasons.Add($"{factory.ImplementationId}: {reason}");
                    continue;
                }
                if (selected is not null)
                {
                    throw new NotSupportedException("Multiple decoders match; register a single implementation for this configuration.");
                }
                selected = factory;
            }
            return selected ?? throw new NotSupportedException(
                $"No decoder supports codec '{stream.CodecId}'. {string.Join("; ", reasons)}");
        }
    }
}

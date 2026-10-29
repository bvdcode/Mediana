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
    public class MediaEngine
    {
        private readonly IContainerReaderFactory[] containers;
        private readonly IVideoDecoderFactory[] decoders;
        private readonly IAudioDecoderFactory[] audioDecoders;

        internal MediaEngine(IContainerReaderFactory[] containers, IVideoDecoderFactory[] decoders, IAudioDecoderFactory[] audioDecoders)
        {
            this.containers = containers;
            this.decoders = decoders;
            this.audioDecoders = audioDecoders;
            AudioDecoderImplementations = Array.AsReadOnly(audioDecoders.Select(item => item.ImplementationId).ToArray());
            ContainerImplementations = Array.AsReadOnly(containers.Select(item => item.ImplementationId).ToArray());
            VideoDecoderImplementations = Array.AsReadOnly(decoders.Select(item => item.ImplementationId).ToArray());
        }

        public IReadOnlyList<string> ContainerImplementations { get; }
        public IReadOnlyList<string> VideoDecoderImplementations { get; }
        public IReadOnlyList<string> AudioDecoderImplementations { get; }

        /// <summary>Opens the explicitly selected stream. Borrows the byte source until the returned session is disposed.
        /// Sequential sources are consumed even on failure. Random access sources open at their origin
        /// without changing the caller's sequential position. No temporary file is used.</summary>
        public async ValueTask<VideoSession> OpenVideoAsync(IByteSource source, int streamIndex,
            MediaOpenOptions? options = null, CancellationToken cancellationToken = default) =>
            new(await OpenCoreAsync(source, streamIndex, MediaKind.Video, decoders, options, cancellationToken).ConfigureAwait(false));

        /// <summary>Opens the selected audio stream. Borrows the source; frames belong to the caller.
        /// Sequential sources are consumed on failure. Random access opening preserves the caller's cursor.</summary>
        public async ValueTask<AudioSession> OpenAudioAsync(IByteSource source, int streamIndex,
            MediaOpenOptions? options = null, CancellationToken cancellationToken = default) =>
            new(await OpenCoreAsync(source, streamIndex, MediaKind.Audio, audioDecoders, options, cancellationToken).ConfigureAwait(false));

        private async ValueTask<MediaDecodeSession<TFrame>> OpenCoreAsync<TFrame>(IByteSource source, int streamIndex,
            MediaKind kind, IReadOnlyList<IDecoderFactory<TFrame>> factories, MediaOpenOptions? options, CancellationToken cancellationToken)
            where TFrame : class, IDisposable
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentOutOfRangeException.ThrowIfNegative(streamIndex);
            options ??= new MediaOpenOptions();
            options.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            if (containers.Length == 0)
            {
                throw new NotSupportedException("No container readers are registered.");
            }
            int probeLength = containers.Max(item => item.ProbeLength);
            if (probeLength > options.MaxProbeBytes)
            {
                throw new NotSupportedException("Registered container probing exceeds the configured probe limit.");
            }
            MediaBufferAllocator allocator = new(options.BufferBudgetBytes);
            using WritableMediaBuffer writable = allocator.Allocate(probeLength);
            int read = 0;
            while (read < probeLength)
            {
                int count = source is IRandomAccessByteSource randomSource
                    ? await randomSource.ReadAtAsync(read, writable.Memory[read..], cancellationToken).ConfigureAwait(false)
                    : await source.ReadAsync(writable.Memory[read..], cancellationToken).ConfigureAwait(false);
                if (count < 0 || count > probeLength - read)
                {
                    throw new InvalidDataException("The source returned an invalid read length.");
                }
                if (count == 0)
                {
                    break;
                }
                read += count;
            }
            cancellationToken.ThrowIfCancellationRequested();
            IContainerReaderFactory factory = SelectContainer(writable.Memory.Span[..read]);
            using MediaBuffer prefix = writable.Publish();
            IByteSource replay = source is IRandomAccessByteSource randomAccess
                ? new RandomAccessCursorByteSource(randomAccess, leaveOpen: true)
                : new PrefixByteSource(source, prefix, read, leaveOpen: true);
            IContainerReader? reader = null;
            IMediaCodec<MediaPacket, TFrame>? decoder = null;
            MediaDecodePipeline<TFrame>? pipeline = null;
            bool transferred = false;
            try
            {
                reader = await factory.OpenAsync(replay, allocator, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                EncodedStreamInfo stream = reader.Streams.SingleOrDefault(item => item.StreamIndex == streamIndex)
                    ?? throw new ArgumentException("The requested stream does not exist.", nameof(streamIndex));
                if (stream.Kind != kind)
                {
                    throw new NotSupportedException("The requested stream has a different media kind.");
                }
                decoder = SelectDecoder(stream, factories).Create(stream, allocator);
                pipeline = new MediaDecodePipeline<TFrame>(reader, decoder, streamIndex, options.MaxPacketReads);
                MediaDecodeSession<TFrame> session = new(reader, replay, pipeline, allocator, options.MaxFrameReads, stream);
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
                        try
                        {
                            if (reader is not null)
                            {
                                await reader.DisposeAsync().ConfigureAwait(false);
                            }
                        }
                        finally
                        {
                            await replay.DisposeAsync().ConfigureAwait(false);
                        }
                    }
                }
            }
        }

        private IContainerReaderFactory SelectContainer(ReadOnlySpan<byte> prefix)
        {
            IContainerReaderFactory? selected = null;
            foreach (IContainerReaderFactory factory in containers)
            {
                if (factory.CanRead(prefix))
                {
                    if (selected is not null)
                    {
                        throw new NotSupportedException("Multiple container readers match; register a single implementation for this input.");
                    }
                    selected = factory;
                }
            }
            return selected ?? throw new NotSupportedException("No registered container reader recognizes the input.");
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

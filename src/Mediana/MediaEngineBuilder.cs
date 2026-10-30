// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Containers;
using Mediana.Core.Audio;
using Mediana.Core.Video;

namespace Mediana
{
    /// <summary>Explicit registration without runtime discovery. Build produces an independent registry snapshot.</summary>
    public class MediaEngineBuilder
    {
        private readonly List<IContainerReaderFactory> containers = [];
        private readonly List<IVideoDecoderFactory> decoders = [];
        private readonly List<IAudioDecoderFactory> audioDecoders = [];
        private readonly List<IStreamMetadataReader> metadataReaders = [];
        private readonly List<IImageDecoder> imageDecoders = [];

        public MediaEngineBuilder AddContainer(IContainerReaderFactory factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            ArgumentException.ThrowIfNullOrWhiteSpace(factory.ImplementationId);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(factory.ProbeLength);
            if (containers.Any(item => item.ImplementationId == factory.ImplementationId))
            {
                throw new ArgumentException($"Container implementation '{factory.ImplementationId}' is already registered.", nameof(factory));
            }
            containers.Add(factory);
            return this;
        }

        public MediaEngineBuilder AddVideoDecoder(IVideoDecoderFactory factory)
        {
            ValidateDecoder<VideoFrame>(factory, decoders);
            decoders.Add(factory);
            return this;
        }

        public MediaEngineBuilder AddAudioDecoder(IAudioDecoderFactory factory)
        {
            ValidateDecoder<AudioFrame>(factory, audioDecoders);
            audioDecoders.Add(factory);
            return this;
        }

        public MediaEngineBuilder AddStreamMetadataReader(IStreamMetadataReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader);
            ArgumentException.ThrowIfNullOrWhiteSpace(reader.ImplementationId);
            ArgumentException.ThrowIfNullOrWhiteSpace(reader.CodecId);
            if (metadataReaders.Any(item => item.ImplementationId == reader.ImplementationId || item.CodecId == reader.CodecId))
            {
                throw new ArgumentException("A metadata reader with the same implementation or codec identifier is already registered.", nameof(reader));
            }
            metadataReaders.Add(reader);
            return this;
        }

        public MediaEngineBuilder AddImageDecoder(IImageDecoder decoder)
        {
            ArgumentNullException.ThrowIfNull(decoder);
            ArgumentException.ThrowIfNullOrWhiteSpace(decoder.ImplementationId);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(decoder.ProbeLength);
            new ImageDecodeRequest().Validate(decoder.Features);
            if (imageDecoders.Any(item => item.ImplementationId == decoder.ImplementationId))
            {
                throw new ArgumentException($"Image decoder '{decoder.ImplementationId}' is already registered.", nameof(decoder));
            }
            imageDecoders.Add(decoder);
            return this;
        }

        private static void ValidateDecoder<TFrame>(IDecoderFactory<TFrame> factory, IEnumerable<IDecoderFactory<TFrame>> existing)
            where TFrame : class, IDisposable
        {
            ArgumentNullException.ThrowIfNull(factory);
            ArgumentException.ThrowIfNullOrWhiteSpace(factory.ImplementationId);
            ArgumentException.ThrowIfNullOrWhiteSpace(factory.CodecId);
            if (existing.Any(item => item.ImplementationId == factory.ImplementationId))
            {
                throw new ArgumentException($"Decoder implementation '{factory.ImplementationId}' is already registered.", nameof(factory));
            }
        }

        public MediaEngine Build() => new(containers.ToArray(), decoders.ToArray(), audioDecoders.ToArray(), metadataReaders.ToArray(), imageDecoders.ToArray());
    }
}

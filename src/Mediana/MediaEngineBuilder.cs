// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Containers;
using Mediana.Core.Audio;
using Mediana.Core.Video;
using Mediana.Core;

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
        private readonly List<IAudioEncoderFactory> audioEncoders = [];
        private readonly List<IVideoEncoderFactory> videoEncoders = [];
        private readonly List<IContainerWriterFactory> writers = [];
        private MediaResourceSet resources = MediaResourceSet.Empty;

        /// <summary>Shares explicit resource providers with image and document decoding, including nested resources.</summary>
        public MediaEngineBuilder UseResources(MediaResourceSet resources)
        {
            ArgumentNullException.ThrowIfNull(resources);
            this.resources = resources;
            return this;
        }

        public MediaEngineBuilder AddAudioEncoder(IAudioEncoderFactory factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            ValidateOutputRegistration(factory.ImplementationId, factory.CodecId, audioEncoders.Select(item => (item.ImplementationId, item.CodecId)));
            audioEncoders.Add(factory);
            return this;
        }

        public MediaEngineBuilder AddVideoEncoder(IVideoEncoderFactory factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            ValidateOutputRegistration(factory.ImplementationId, factory.CodecId, videoEncoders.Select(item => (item.ImplementationId, item.CodecId)));
            videoEncoders.Add(factory);
            return this;
        }

        public MediaEngineBuilder AddContainerWriter(IContainerWriterFactory factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            ValidateOutputRegistration(factory.ImplementationId, factory.FormatId, writers.Select(item => (item.ImplementationId, item.FormatId)));
            writers.Add(factory);
            return this;
        }

        private static void ValidateOutputRegistration(string implementation, string format, IEnumerable<(string Implementation, string Format)> existing)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(implementation);
            ArgumentException.ThrowIfNullOrWhiteSpace(format);
            if (existing.Any(item => item.Implementation == implementation || item.Format == format))
            {
                throw new ArgumentException("An output implementation for this identifier is already registered.");
            }
        }

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
            ValidateImageDecoder(decoder);
            if (imageDecoders.Any(item => item.ImplementationId == decoder.ImplementationId))
            {
                throw new ArgumentException($"Image decoder '{decoder.ImplementationId}' is already registered.", nameof(decoder));
            }
            imageDecoders.Add(decoder);
            return this;
        }

        /// <summary>Reconfigures an existing image implementation while preserving all other resource decoders.</summary>
        public MediaEngineBuilder ReplaceImageDecoder(IImageDecoder decoder)
        {
            ValidateImageDecoder(decoder);
            int index = imageDecoders.FindIndex(item => item.ImplementationId == decoder.ImplementationId);
            if (index < 0) { throw new ArgumentException("No image decoder with this implementation identifier is registered.", nameof(decoder)); }
            imageDecoders[index] = decoder;
            return this;
        }

        private static void ValidateImageDecoder(IImageDecoder decoder)
        {
            ArgumentNullException.ThrowIfNull(decoder);
            ArgumentException.ThrowIfNullOrWhiteSpace(decoder.ImplementationId);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(decoder.ProbeLength);
            new ImageDecodeRequest().Validate(decoder.Features);
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

        public MediaEngine Build() => new(containers.ToArray(), decoders.ToArray(), audioDecoders.ToArray(), metadataReaders.ToArray(), imageDecoders.ToArray(), audioEncoders.ToArray(), videoEncoders.ToArray(), writers.ToArray(), resources);
    }
}

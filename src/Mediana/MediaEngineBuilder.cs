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

        public MediaEngine Build() => new(containers.ToArray(), decoders.ToArray(), audioDecoders.ToArray());
    }
}

using Mediana.Codecs;
using Mediana.Containers;

namespace Mediana
{
    /// <summary>Explicit registration without runtime discovery. Build produces an independent registry snapshot.</summary>
    public class MediaEngineBuilder
    {
        private readonly List<IContainerReaderFactory> containers = [];
        private readonly List<IVideoDecoderFactory> decoders = [];

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
            ArgumentNullException.ThrowIfNull(factory);
            ArgumentException.ThrowIfNullOrWhiteSpace(factory.ImplementationId);
            ArgumentException.ThrowIfNullOrWhiteSpace(factory.CodecId);
            if (decoders.Any(item => item.ImplementationId == factory.ImplementationId))
            {
                throw new ArgumentException($"Decoder implementation '{factory.ImplementationId}' is already registered.", nameof(factory));
            }
            decoders.Add(factory);
            return this;
        }

        public MediaEngine Build() => new(containers.ToArray(), decoders.ToArray());
    }
}

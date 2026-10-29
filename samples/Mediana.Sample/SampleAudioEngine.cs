using Mediana.Codecs.Aac;
using Mediana.Codecs.Flac;
using Mediana.Containers.Flac;
using Mediana.Codecs.Pcm;
using Mediana.Containers.Mp4;
using Mediana.Containers.Wave;

namespace Mediana.Sample
{
    internal static class SampleAudioEngine
    {
        public static MediaEngine Create() => new MediaEngineBuilder()
            .AddContainer(new FlacReaderFactory())
            .AddContainer(new WaveReaderFactory())
            .AddContainer(new Mp4ReaderFactory())
            .AddAudioDecoder(new FlacDecoderFactory())
            .AddAudioDecoder(new PcmDecoderFactory())
            .AddAudioDecoder(new AacDecoderFactory())
            .Build();
    }
}

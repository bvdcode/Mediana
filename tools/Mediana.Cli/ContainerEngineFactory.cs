// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Microsoft.Extensions.Logging;
using Mediana.Codecs.Av1;
using Mediana.Codecs.H264;
using Mediana.Codecs.Hevc;
using Mediana.Codecs.Jpeg;
using Mediana.Codecs.ProRes;
using Mediana.Codecs.Dv;
using Mediana.Codecs.Vp8;
using Mediana.Codecs.Vp9;
using Mediana.Codecs.Theora;
using Mediana.Codecs.Mpeg4Visual;
using Mediana.Containers.Aiff;
using Mediana.Containers.Ac3;
using Mediana.Containers.Adts;
using Mediana.Containers.Avi;
using Mediana.Containers.Flac;
using Mediana.Containers.Mp3;
using Mediana.Containers.Mp4;
using Mediana.Containers.Matroska;
using Mediana.Containers.Ogg;
using Mediana.Containers.Wave;
using Mediana.Formats.Video;

namespace Mediana.Cli
{
    internal static class ContainerEngineFactory
    {
        public static MediaEngineBuilder CreateVideoBuilder(ILogger logger, Mp4TimestampMode timestampMode = Mp4TimestampMode.Media)
        {
            ArgumentNullException.ThrowIfNull(logger);
            Action<string> warningHandler = message => logger.LogWarning("{Warning}", message);
            return CreateBuilder(timestampMode)
                .AddVideoDecoder(new H264DecoderFactory(new() { IgnoreInconsistentPictureTiming = true }, warningHandler))
                .AddVideoDecoder(new HevcDecoderFactory())
                .AddVideoDecoder(new ProResDecoderFactory(new() { RecoverLegacyAlpha = true }, warningHandler))
                .AddVideoDecoder(new DvDecoderFactory())
                .AddVideoDecoder(new MjpegDecoderFactory(warningHandler: warningHandler))
                .AddVideoDecoder(new Vp8DecoderFactory())
                .AddVideoDecoder(new Vp9DecoderFactory())
                .AddVideoDecoder(new Mpeg4DecoderFactory())
                .AddVideoDecoder(new MicrosoftMpeg4DecoderFactory(MicrosoftMpeg4Configuration.Version3CodecId))
                .AddVideoDecoder(new MicrosoftMpeg4DecoderFactory(MicrosoftMpeg4Configuration.Wmv2CodecId))
                .AddVideoDecoder(new TheoraDecoderFactory())
                .AddVideoDecoder(new Av1DecoderFactory());
        }

        public static MediaEngineBuilder CreateBuilder(Mp4TimestampMode timestampMode = Mp4TimestampMode.Media) => new MediaEngineBuilder()
            .AddContainer(new AdtsReaderFactory())
            .AddContainer(new Ac3ReaderFactory())
            .AddContainer(new Mp3ReaderFactory())
            .AddContainer(new FlacReaderFactory())
            .AddContainer(new WaveReaderFactory())
            .AddContainer(new AiffReaderFactory())
            .AddContainer(new MatroskaReaderFactory())
            .AddContainer(new AviReaderFactory())
            .AddContainer(new OggReaderFactory())
            .AddContainer(new Mp4ReaderFactory(new Mp4ReadOptions { TimestampMode = timestampMode }));
    }
}

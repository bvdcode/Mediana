// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Codecs.Hevc;
using Mediana.Codecs.Jpeg;
using Mediana.Codecs.Vp8;
using Mediana.Codecs.Vp9;
using Mediana.Codecs.Theora;
using Mediana.Codecs.Mpeg4Visual;
using Mediana.Containers.Aiff;
using Mediana.Containers.Adts;
using Mediana.Containers.Avi;
using Mediana.Containers.Flac;
using Mediana.Containers.Mp3;
using Mediana.Containers.Mp4;
using Mediana.Containers.Matroska;
using Mediana.Containers.Ogg;
using Mediana.Containers.Wave;

namespace Mediana.Cli
{
    internal static class ContainerEngineFactory
    {
        public static MediaEngineBuilder CreateVideoBuilder(Mp4TimestampMode timestampMode = Mp4TimestampMode.Media) =>
            CreateBuilder(timestampMode).AddVideoDecoder(new H264DecoderFactory()).AddVideoDecoder(new HevcDecoderFactory())
                .AddVideoDecoder(new MjpegDecoderFactory()).AddVideoDecoder(new Vp8DecoderFactory()).AddVideoDecoder(new Vp9DecoderFactory()).AddVideoDecoder(new Mpeg4DecoderFactory()).AddVideoDecoder(new TheoraDecoderFactory());

        public static MediaEngineBuilder CreateBuilder(Mp4TimestampMode timestampMode = Mp4TimestampMode.Media) => new MediaEngineBuilder()
            .AddContainer(new AdtsReaderFactory())
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

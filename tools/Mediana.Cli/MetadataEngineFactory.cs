// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Aac;
using Mediana.Codecs.Av1;
using Mediana.Codecs.Hevc;
using Mediana.Codecs.Jpeg;
using Mediana.Codecs.Mpeg4Visual;
using Mediana.Codecs.Vp9;
using Mediana.Codecs.Ac3;
using Mediana.Codecs.Flac;
using Mediana.Codecs.Alac;
using Mediana.Codecs.Dts;
using Mediana.Codecs.H264;
using Mediana.Codecs.Dv;
using Mediana.Codecs.Ffv1;
using Mediana.Codecs.Mp3;
using Mediana.Codecs.Pcm;
using Mediana.Codecs.ProRes;
using Mediana.Formats.Audio;
using Mediana.Formats.Video;

namespace Mediana.Cli
{
    internal static class MetadataEngineFactory
    {
        public static MediaEngine Create() => ImageEngineFactory.AddDecoders(ContainerEngineFactory.CreateBuilder())
            .AddStreamMetadataReader(new PcmStreamMetadataReader())
            .AddStreamMetadataReader(new AiffPcmStreamMetadataReader())
            .AddStreamMetadataReader(new AiffIma4Codec())
            .AddStreamMetadataReader(new WaveCompressedAudioCodec(WaveCompressedAudioEncoding.ALaw))
            .AddStreamMetadataReader(new WaveCompressedAudioCodec(WaveCompressedAudioEncoding.MuLaw))
            .AddStreamMetadataReader(new WaveCompressedAudioCodec(WaveCompressedAudioEncoding.ImaAdpcm))
            .AddStreamMetadataReader(new WaveCompressedAudioCodec(WaveCompressedAudioEncoding.MicrosoftAdpcm))
            .AddStreamMetadataReader(new FlacStreamMetadataReader())
            .AddStreamMetadataReader(new AlacStreamMetadataReader())
            .AddStreamMetadataReader(new DtsStreamMetadataReader())
            .AddStreamMetadataReader(new ProResStreamMetadataReader())
            .AddStreamMetadataReader(new Mp3StreamMetadataReader())
            .AddStreamMetadataReader(new AacStreamMetadataReader())
            .AddStreamMetadataReader(new Ac3StreamMetadataReader())
            .AddStreamMetadataReader(new Eac3StreamMetadataReader())
            .AddStreamMetadataReader(new DvStreamMetadataReader())
            .AddStreamMetadataReader(new Ffv1StreamMetadataReader())
            .AddStreamMetadataReader(new HevcStreamMetadataReader())
            .AddStreamMetadataReader(new Av1StreamMetadataReader())
            .AddStreamMetadataReader(new Vp9StreamMetadataReader())
            .AddStreamMetadataReader(new Mpeg4StreamMetadataReader())
            .AddStreamMetadataReader(new MicrosoftMpeg4StreamMetadataReader(MicrosoftMpeg4Configuration.Version3CodecId))
            .AddStreamMetadataReader(new MicrosoftMpeg4StreamMetadataReader(MicrosoftMpeg4Configuration.Wmv2CodecId))
            .AddStreamMetadataReader(new MjpegStreamMetadataReader())
            .AddStreamMetadataReader(new H264StreamMetadataReader())
            .Build();
    }
}

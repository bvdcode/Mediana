// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Aac;
using Mediana.Codecs.Ac3;
using Mediana.Codecs.Mp3;
using Mediana.Codecs.Opus;
using Mediana.Codecs.Vorbis;
using Mediana.Codecs.Flac;
using Mediana.Codecs.Pcm;
using Mediana.Containers.Mp4;

namespace Mediana.Cli
{
    internal static class AudioEngineFactory
    {
        public static MediaEngine Create(Mp4TimestampMode timestampMode = Mp4TimestampMode.Media) => CreateBuilder(timestampMode).Build();

        public static MediaEngineBuilder CreateBuilder(Mp4TimestampMode timestampMode = Mp4TimestampMode.Media) => ContainerEngineFactory.CreateBuilder(timestampMode)
            .AddAudioDecoder(new FlacDecoderFactory())
            .AddAudioDecoder(new PcmDecoderFactory())
            .AddAudioDecoder(new AiffPcmDecoderFactory())
            .AddAudioDecoder(new AacDecoderFactory())
            .AddAudioDecoder(new Ac3DecoderFactory())
            .AddAudioDecoder(new Eac3DecoderFactory())
            .AddAudioDecoder(new Mp3DecoderFactory())
            .AddAudioDecoder(new OpusDecoderFactory())
            .AddAudioDecoder(new VorbisDecoderFactory());
    }
}

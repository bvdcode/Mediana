// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Aac;
using Mediana.Codecs.Mp3;
using Mediana.Codecs.Flac;
using Mediana.Codecs.Pcm;

namespace Mediana.Cli
{
    internal static class AudioEngineFactory
    {
        public static MediaEngine Create() => ContainerEngineFactory.CreateBuilder()
            .AddAudioDecoder(new FlacDecoderFactory())
            .AddAudioDecoder(new PcmDecoderFactory())
            .AddAudioDecoder(new AiffPcmDecoderFactory())
            .AddAudioDecoder(new AacDecoderFactory())
            .AddAudioDecoder(new Mp3DecoderFactory())
            .Build();
    }
}

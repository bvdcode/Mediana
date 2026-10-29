// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Aac;
using Mediana.Codecs.Mp3;
using Mediana.Containers.Mp3;
using Mediana.Codecs.Flac;
using Mediana.Containers.Flac;
using Mediana.Codecs.Pcm;
using Mediana.Containers.Mp4;
using Mediana.Containers.Wave;

namespace Mediana.Cli
{
    internal static class AudioEngineFactory
    {
        public static MediaEngine Create() => new MediaEngineBuilder()
            .AddContainer(new Mp3ReaderFactory())
            .AddContainer(new FlacReaderFactory())
            .AddContainer(new WaveReaderFactory())
            .AddContainer(new Mp4ReaderFactory())
            .AddAudioDecoder(new FlacDecoderFactory())
            .AddAudioDecoder(new PcmDecoderFactory())
            .AddAudioDecoder(new AacDecoderFactory())
            .AddAudioDecoder(new Mp3DecoderFactory())
            .Build();
    }
}

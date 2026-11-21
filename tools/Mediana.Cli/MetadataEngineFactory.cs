// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Aac;
using Mediana.Codecs.Ac3;
using Mediana.Codecs.Flac;
using Mediana.Codecs.Alac;
using Mediana.Codecs.H264;
using Mediana.Codecs.Mp3;
using Mediana.Codecs.Pcm;
using Mediana.Codecs.ProRes;

namespace Mediana.Cli
{
    internal static class MetadataEngineFactory
    {
        public static MediaEngine Create() => ImageEngineFactory.AddDecoders(ContainerEngineFactory.CreateBuilder())
            .AddStreamMetadataReader(new PcmStreamMetadataReader())
            .AddStreamMetadataReader(new AiffPcmStreamMetadataReader())
            .AddStreamMetadataReader(new FlacStreamMetadataReader())
            .AddStreamMetadataReader(new AlacStreamMetadataReader())
            .AddStreamMetadataReader(new ProResStreamMetadataReader())
            .AddStreamMetadataReader(new Mp3StreamMetadataReader())
            .AddStreamMetadataReader(new AacStreamMetadataReader())
            .AddStreamMetadataReader(new Ac3StreamMetadataReader())
            .AddStreamMetadataReader(new Eac3StreamMetadataReader())
            .AddStreamMetadataReader(new H264StreamMetadataReader()).Build();
    }
}

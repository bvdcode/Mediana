// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Aiff;
using Mediana.Containers.Flac;
using Mediana.Containers.Mp3;
using Mediana.Containers.Mp4;
using Mediana.Containers.Wave;

namespace Mediana.Cli
{
    internal static class ContainerEngineFactory
    {
        public static MediaEngineBuilder CreateBuilder(Mp4TimestampMode timestampMode = Mp4TimestampMode.Media) => new MediaEngineBuilder()
            .AddContainer(new Mp3ReaderFactory())
            .AddContainer(new FlacReaderFactory())
            .AddContainer(new WaveReaderFactory())
            .AddContainer(new AiffReaderFactory())
            .AddContainer(new Mp4ReaderFactory(new Mp4ReadOptions { TimestampMode = timestampMode }));
    }
}

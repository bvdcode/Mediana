// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Containers.Mp4;
using Mediana.Core.Timing;
using Mediana.IO;
using Microsoft.Extensions.Logging;

namespace Mediana.Cli
{
    internal static class VideoPreviewCommand
    {
        public static async Task<int> RunAsync(string inputPath, CommandOutputFile outputFile, int? streamIndex, long milliseconds,
            int maximumWidth, int maximumHeight, ILogger logger, IImageEncoder encoder, CancellationToken cancellationToken = default)
        {
            MediaEngine engine = ContainerEngineFactory.CreateVideoBuilder(Mp4TimestampMode.LinearMovie).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(inputPath));
            VisualOpenOptions options = new()
            {
                StreamIndex = streamIndex,
                Request = new() { Time = new MediaTimestamp(milliseconds, new TimeBase(1, 1000)) }
            };
            return await ImagePreviewCommand.RunAsync(engine, source, outputFile, logger, encoder, options.Request, cancellationToken,
                options, maximumWidth, maximumHeight);
        }
    }
}

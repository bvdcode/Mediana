// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Timing;
using Mediana.IO;
using Microsoft.Extensions.Logging;

namespace Mediana.Cli
{
    internal static class FilePreviewCommand
    {
        public static async Task<int> RunAsync(string input, CommandOutputFile outputFile, int? stream, long milliseconds, int page, ILogger logger, IImageEncoder encoder, CancellationToken token)
        {
            await using RandomAccessStreamByteSource source = new(File.OpenRead(input));
            VisualOpenOptions options = new()
            {
                StreamIndex = stream, Images = CommandImageResources.OpenOptions,
                Request = new() { PageIndex = page - 1, Time = new MediaTimestamp(milliseconds, new TimeBase(1, 1000)) }
            };
            return await ImagePreviewCommand.RunAsync(ImageEngineFactory.CreateVisual(), source, outputFile, logger, encoder,
                options.Request, token, options);
        }
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Microsoft.Extensions.Logging;

namespace Mediana.Cli
{
    internal record ImageConversionRequest(IImageEncoder Encoder, long TimeMs, int Page) : ConversionRequest
    {
        public override Task<int> ExecuteAsync(string input, CommandOutputFile output, int? stream,
            ILogger logger, TextWriter writer, CancellationToken token) =>
            FilePreviewCommand.RunAsync(input, output, stream, TimeMs, Page, logger, Encoder, token);
    }
}

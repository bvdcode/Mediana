// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Microsoft.Extensions.Logging;

namespace Mediana.Cli
{
    internal record AnimationConversionRequest(IImageSequenceEncoder Encoder, long TimeMs) : ConversionRequest
    {
        public override Task<int> ExecuteAsync(string input, CommandOutputFile output, int? stream,
            ILogger logger, TextWriter writer, CancellationToken token) =>
            ImageSequenceCommand.RunAsync(input, output, stream, TimeMs, logger, writer, Encoder, token);
    }
}

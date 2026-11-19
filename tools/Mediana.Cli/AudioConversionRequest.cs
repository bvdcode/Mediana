// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Microsoft.Extensions.Logging;

namespace Mediana.Cli
{
    internal record AudioConversionRequest(int BitRate, int? Channels) : ConversionRequest
    {
        public override Task<int> ExecuteAsync(string input, CommandOutputFile output, int? stream,
            ILogger logger, TextWriter writer, CancellationToken token) =>
            AudioEncodeCommand.RunAsync(input, output, stream, BitRate, Channels, logger, token);
    }
}

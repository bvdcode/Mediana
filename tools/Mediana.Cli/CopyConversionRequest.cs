// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Microsoft.Extensions.Logging;

namespace Mediana.Cli
{
    internal record CopyConversionRequest : ConversionRequest
    {
        public override Task<int> ExecuteAsync(string input, CommandOutputFile output, int? stream,
            ILogger logger, TextWriter writer, CancellationToken token) =>
            MpegTsRemuxCommand.RunAsync(input, output, stream, token);
    }
}

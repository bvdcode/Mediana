// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Microsoft.Extensions.Logging;

namespace Mediana.Cli
{
    internal abstract record ConversionRequest
    {
        public abstract Task<int> ExecuteAsync(string input, CommandOutputFile output, int? stream,
            ILogger logger, TextWriter writer, CancellationToken token);
    }
}

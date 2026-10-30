// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Mediana.Cli
{
    internal class CliOperation(ILogger logger)
    {
        private string? name;
        private long started;

        internal void Start(string operation)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(operation);
            if (name is not null)
            {
                throw new InvalidOperationException("A command operation has already started.");
            }
            name = operation;
            started = Stopwatch.GetTimestamp();
            CliLog.Starting(logger, operation);
        }

        internal void Complete()
        {
            if (name is null)
            {
                throw new InvalidOperationException("No command operation has started.");
            }
            CliLog.Completed(logger, name, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }
}

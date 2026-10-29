// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Microsoft.Extensions.Logging;

namespace Mediana.Cli
{
    internal static partial class CliLog
    {
        [LoggerMessage(1, LogLevel.Debug, "Starting {Operation}.")]
        public static partial void Starting(ILogger logger, string operation);

        [LoggerMessage(2, LogLevel.Information, "{Operation} completed in {ElapsedMilliseconds:F0} ms.")]
        public static partial void Completed(ILogger logger, string operation, double elapsedMilliseconds);

        [LoggerMessage(3, LogLevel.Error, "{Message}")]
        public static partial void Failed(ILogger logger, string message);

        [LoggerMessage(4, LogLevel.Debug, "Command failure details.")]
        public static partial void FailureDetails(ILogger logger, Exception exception);

        [LoggerMessage(5, LogLevel.Warning, "Operation cancelled.")]
        public static partial void Cancelled(ILogger logger);
    }
}

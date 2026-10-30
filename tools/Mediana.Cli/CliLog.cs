// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Microsoft.Extensions.Logging;

namespace Mediana.Cli
{
    internal static partial class CliLog
    {
        [LoggerMessage(1, LogLevel.Information, "Starting {Operation}.")]
        public static partial void Starting(ILogger logger, string operation);

        [LoggerMessage(2, LogLevel.Information, "{Operation} completed in {ElapsedMilliseconds:F0} ms.")]
        public static partial void Completed(ILogger logger, string operation, double elapsedMilliseconds);

        [LoggerMessage(3, LogLevel.Error, "{Message}")]
        public static partial void Failed(ILogger logger, string message);

        [LoggerMessage(4, LogLevel.Debug, "Command failure details.")]
        public static partial void FailureDetails(ILogger logger, Exception exception);

        [LoggerMessage(5, LogLevel.Warning, "Operation cancelled.")]
        public static partial void Cancelled(ILogger logger);

        [LoggerMessage(6, LogLevel.Information, "time={MediaTime} speed={Speed:F2}x size={OutputMebibytes:F2} MiB")]
        public static partial void Progress(ILogger logger, string mediaTime, double speed, double outputMebibytes);

        [LoggerMessage(7, LogLevel.Information, "time={MediaTime} speed={Speed:F2}x size={OutputMebibytes:F2} MiB eta={RemainingTime}")]
        public static partial void ProgressWithEstimate(ILogger logger, string mediaTime, double speed, double outputMebibytes, string remainingTime);
    }
}

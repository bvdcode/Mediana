// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace Mediana.Cli
{
    internal class CliConsoleFormatter(CliConsoleFormatterOptions options) : ConsoleFormatter(FormatterName)
    {
        internal const string FormatterName = "compact";

        public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
        {
            if (logEntry.LogLevel is LogLevel.None)
            {
                return;
            }
            string message = logEntry.Formatter(logEntry.State, logEntry.Exception);
            if (message.Length == 0 && logEntry.Exception is null)
            {
                return;
            }
            CliConsoleFormatterOptions settings = options;
            if (settings.TimestampFormat is not null)
            {
                DateTime now = settings.UseUtcTimestamp ? DateTime.UtcNow : DateTime.Now;
                textWriter.Write(now.ToString(settings.TimestampFormat, CultureInfo.InvariantCulture));
            }
            (string label, string color) = logEntry.LogLevel switch
            {
                LogLevel.Trace => ("trace", "\u001b[90m"),
                LogLevel.Debug => ("debug", "\u001b[90m"),
                LogLevel.Information => ("info", "\u001b[32m"),
                LogLevel.Warning => ("warn", "\u001b[33m"),
                LogLevel.Error => ("error", "\u001b[31m"),
                LogLevel.Critical => ("critical", "\u001b[1;31m"),
                LogLevel.None => throw new InvalidOperationException("The disabled log level cannot be formatted."),
                _ => throw new ArgumentOutOfRangeException(nameof(logEntry), logEntry.LogLevel, "Unknown log level.")
            };
            if (settings.ColorsEnabled)
            {
                textWriter.Write(color);
            }
            textWriter.Write(label);
            if (settings.ColorsEnabled)
            {
                textWriter.Write("\u001b[0m");
            }
            textWriter.Write(": ");
            textWriter.Write(message.Replace('\r', ' ').Replace('\n', ' '));
            if (logEntry.Exception is not null)
            {
                textWriter.Write(' ');
                textWriter.Write(logEntry.Exception.ToString().Replace('\r', ' ').Replace('\n', ' '));
            }
            textWriter.WriteLine();
        }
    }
}

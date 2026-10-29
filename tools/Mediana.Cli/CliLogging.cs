// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace Mediana.Cli
{
    internal static class CliLogging
    {
        public static ILoggerFactory Create(LogLevel minimumLevel, bool noColor)
        {
            return LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(minimumLevel);
                builder.AddSimpleConsole(options =>
                {
                    options.SingleLine = true;
                    options.TimestampFormat = "HH:mm:ss ";
                    options.ColorBehavior = ColorsEnabled(noColor)
                        ? LoggerColorBehavior.Enabled : LoggerColorBehavior.Disabled;
                });
                builder.Services.Configure<ConsoleLoggerOptions>(options =>
                    options.LogToStandardErrorThreshold = LogLevel.Trace);
            });
        }

        internal static bool ColorsEnabled(bool noColor) =>
            !noColor && !Console.IsErrorRedirected && Environment.GetEnvironmentVariable("NO_COLOR") is null;
    }
}

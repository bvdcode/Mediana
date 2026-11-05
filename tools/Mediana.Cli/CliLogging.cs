// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace Mediana.Cli
{
    internal static class CliLogging
    {
        public static void Configure(ILoggingBuilder builder, LogLevel minimumLevel, bool noColor)
        {
            builder.SetMinimumLevel(minimumLevel);
            builder.AddConsole(options => options.FormatterName = CliConsoleFormatter.FormatterName);
            builder.Services.AddSingleton<ConsoleFormatter>(new CliConsoleFormatter(new CliConsoleFormatterOptions
            {
                TimestampFormat = "HH:mm:ss ",
                ColorsEnabled = ColorsEnabled(noColor)
            }));
            builder.Services.Configure<ConsoleLoggerOptions>(options =>
                options.LogToStandardErrorThreshold = LogLevel.Trace);
        }

        internal static bool ColorsEnabled(bool noColor) =>
            !noColor && !Console.IsErrorRedirected && Environment.GetEnvironmentVariable("NO_COLOR") is null;
    }
}

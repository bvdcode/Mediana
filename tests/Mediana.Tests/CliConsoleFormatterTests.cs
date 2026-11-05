// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using Mediana.Cli;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mediana.Tests
{
    public class CliConsoleFormatterTests
    {
        [Theory]
        [InlineData(false, "info: time=00:01 speed=2.00x")]
        [InlineData(true, "\u001b[32minfo\u001b[0m: time=00:01 speed=2.00x")]
        public void ProgressHasNoCategoryOrEventNumber(bool colorsEnabled, string expected)
        {
            CliConsoleFormatter formatter = new(new CliConsoleFormatterOptions { ColorsEnabled = colorsEnabled });
            using StringWriter output = new(CultureInfo.InvariantCulture);
            LogEntry<string> entry = new(LogLevel.Information, "mediana", new EventId(7),
                "time=00:01 speed=2.00x", null, (message, _) => message);
            formatter.Write(entry, null, output);
            Assert.Equal(expected + Environment.NewLine, output.ToString());
        }

        [Fact]
        public void FailurePreservesExceptionAndSingleLineMessage()
        {
            CliConsoleFormatter formatter = new(new CliConsoleFormatterOptions());
            using StringWriter output = new(CultureInfo.InvariantCulture);
            LogEntry<string> entry = new(LogLevel.Error, "mediana", new EventId(4),
                "Failure\nDetails", new InvalidOperationException("Invalid input"), (message, _) => message);
            formatter.Write(entry, null, output);
            Assert.Equal("error: Failure Details System.InvalidOperationException: Invalid input" + Environment.NewLine, output.ToString());
        }
    }
}

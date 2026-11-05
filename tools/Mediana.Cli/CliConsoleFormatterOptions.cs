// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Microsoft.Extensions.Logging.Console;

namespace Mediana.Cli
{
    internal class CliConsoleFormatterOptions : ConsoleFormatterOptions
    {
        public bool ColorsEnabled { get; set; }
    }
}

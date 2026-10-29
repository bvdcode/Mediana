// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Cli
{
    internal static class CliHelp
    {
        public static bool IsRequested(IReadOnlyList<string> arguments)
        {
            foreach (string argument in arguments)
            {
                if (argument == "--")
                {
                    break;
                }
                if (argument is "--help" or "-h" or "--version")
                {
                    return true;
                }
            }
            return false;
        }
    }
}

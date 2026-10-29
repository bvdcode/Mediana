// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Cli
{
    internal static class CliExitCodes
    {
        public const int Success = 0;
        public const int Failure = 1;
        public const int Usage = 2;
        public const int Unsupported = 3;
        public const int InvalidInput = 4;
        public const int IoFailure = 5;
        public const int Cancelled = 130;
    }
}

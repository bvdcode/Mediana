// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Streaming.Hls
{
    internal static class HlsAttribute
    {
        public static void Validate(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Any(character => character == '"' || char.IsControl(character)))
            {
                throw new ArgumentException("HLS quoted attributes require nonempty text without quotes or control characters.", nameof(value));
            }
        }
    }
}

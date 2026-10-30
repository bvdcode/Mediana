// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Streaming.Hls
{
    internal static class HlsUri
    {
        public static void Validate(string uri, int maximumCharacters)
        {
            if (string.IsNullOrEmpty(uri) || uri.Length > maximumCharacters || uri[0] == '#' ||
                uri.Any(character => char.IsControl(character) || char.IsWhiteSpace(character)) ||
                !Uri.IsWellFormedUriString(uri, UriKind.RelativeOrAbsolute))
            {
                throw new ArgumentException("HLS URIs must be well-formed, bounded URI lines without whitespace or control characters.", nameof(uri));
            }
        }
    }
}

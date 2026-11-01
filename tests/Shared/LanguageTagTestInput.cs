// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Metadata;

namespace Mediana.Tests
{
    internal static class LanguageTagTestInput
    {
        public static readonly string[] WellFormed =
        [
            "de", "fr", "ja", "i-enochian", "i-klingon", "en-GB-oed", "sgn-BE-FR", "zh-min-nan",
            "zh-Hant", "zh-Hans-CN", "sr-Cyrl", "sr-Latn-RS", "zh-cmn-Hans-CN", "cmn-Hans-CN", "zh-yue-HK",
            "de-CH-1901", "sl-rozaj", "sl-rozaj-biske", "sl-nedis", "en-US", "es-419", "sl-IT-nedis",
            "de-CH-x-phonebk", "az-Arab-x-AZE-derbend", "x-whatever", "qaa-Qaaa-QM-x-southern",
            "de-Qaaa", "sr-Qaaa-RS", "en-US-u-islamcal", "zh-CN-a-myext-x-private", "en-a-myext-b-another",
            "abc-def-ghi-jkl-Latn-001-variant1-0-sample-x-a", "X-A-B-12345678", "en-USA"
        ];

        public static readonly string[] Malformed =
        [
            "", "a", "en_US", "en-", "-en", "en--US", "en-中文", "en-\u001b[31m", "en-1234a6789", "en-abcd-efgh",
            "x", "en-x", "en-a", "en-a-x-private", "en-a-one-a-two", "en-A-one-a-two", "de-1901-1901", "sl-rozaj-ROZAJ",
            "abcdefghi", "123", "en-Latn-US-Cyrl", "en-US-US", "abc-def-ghi-jkl-mno", "i-notreal", "en-ü"
        ];

        public static void Verify()
        {
            foreach (string value in WellFormed)
            {
                if (!Bcp47LanguageTag.IsWellFormed(value))
                {
                    throw new InvalidDataException($"A well-formed RFC 5646 language tag was rejected: {value}.");
                }
            }
            foreach (string value in Malformed)
            {
                if (Bcp47LanguageTag.IsWellFormed(value))
                {
                    throw new InvalidDataException("A malformed language tag was accepted.");
                }
            }
        }
    }
}

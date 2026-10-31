// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Mp4
{
    /// <summary>Original mdhd/elng declarations and their selected language identifier.</summary>
    public class Mp4LanguageInfo
    {
        internal Mp4LanguageInfo(ushort code, string? extendedTag, string? language)
        {
            Code = code;
            ExtendedTag = extendedTag;
            Language = language;
        }

        public ushort Code { get; }
        /// <summary>Original elng string; null means absent. Invalid ASCII bytes retain their Latin-1 code points.</summary>
        public string? ExtendedTag { get; }
        /// <summary>Selected ISO 639-2/T or BCP 47 identifier. Null means unspecified or unrecognized;
        /// an invalid extended tag does not select the older mdhd declaration.</summary>
        public string? Language { get; }
    }
}

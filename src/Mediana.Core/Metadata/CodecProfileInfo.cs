// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Core.Metadata
{
    /// <summary>Named codec configuration declarations, not verification that the complete stream conforms to them.</summary>
    public class CodecProfileInfo
    {
        public CodecProfileInfo(string name, string? level = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            if (level is not null)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(level);
            }
            Name = name;
            Level = level;
        }

        public string Name { get; }
        /// <summary>Named declared level, or null when absent or unrecognized. Levels are codec-specific identifiers.</summary>
        public string? Level { get; }
    }
}

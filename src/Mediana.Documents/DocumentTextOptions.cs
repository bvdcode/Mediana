// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents
{
    /// <summary>Part selection is applied before reading content. Limits reject excessive output; use Take or break for early completion.</summary>
    public record DocumentTextOptions
    {
        public Action<string>? WarningHandler { get; init; }
        public int StartPartIndex { get; init; }
        public int? PartCount { get; init; }
        public int MaximumBlocks { get; init; } = 1_000_000;
        public long MaximumCharacters { get; init; } = 64L * 1024 * 1024;

        public void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfNegative(StartPartIndex);
            if (PartCount is <= 0 || MaximumBlocks <= 0 || MaximumCharacters <= 0)
            { throw new ArgumentOutOfRangeException(nameof(MaximumBlocks), "Text selection and output limits must be positive."); }
        }

        public bool IncludesPart(int index) => index >= StartPartIndex && (PartCount is null || (long)index - StartPartIndex < PartCount.Value);
    }
}

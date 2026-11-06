// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Spreadsheets
{
    /// <summary>Formula source and cache presence. BinaryTokens contain BIFF tokens; XML formulas retain text and shared/array metadata.
    /// No formula is evaluated. Shared followers can have no Text and refer to SharedIndex.</summary>
    public record SpreadsheetFormula(string? Text, ReadOnlyMemory<byte> BinaryTokens, bool HasCachedValue,
        int? SharedIndex = null, string? Reference = null, SpreadsheetFormulaKind Kind = SpreadsheetFormulaKind.Normal)
    {
        /// <summary>BIFF array and auxiliary formula data following the token stream. Empty for XML formulas.</summary>
        public ReadOnlyMemory<byte> BinaryExtraData { get; init; }
    }
}

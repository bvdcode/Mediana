// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Collections.ObjectModel;

namespace Mediana.Drawing
{
    /// <summary>Explicit font discovery bounds and reusable text resource configuration. Inline artwork remains borrowed.</summary>
    public record TextResourceOptions
    {
        public int MaximumFontBytes { get; init; } = 64 * 1024 * 1024;
        public int MaximumFonts { get; init; } = 256;
        public bool SubstituteMissingFonts { get; init; }
        public IInlineGraphicProvider? InlineGraphicProvider { get; init; }
        public IReadOnlyDictionary<string, string> FamilyAliases { get; init; } = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sans-serif"] = "Liberation Sans",
            ["Helvetica"] = "Liberation Sans",
            ["Arial"] = "Liberation Sans",
            ["ArialMT"] = "Liberation Sans",
            ["serif"] = "Liberation Serif",
            ["Times"] = "Liberation Serif",
            ["Times New Roman"] = "Liberation Serif",
            ["TimesNewRomanPSMT"] = "Liberation Serif",
            ["monospace"] = "Liberation Mono",
            ["Courier"] = "Liberation Mono",
            ["Courier New"] = "Liberation Mono",
            ["CourierNewPSMT"] = "Liberation Mono",
            ["Calibri"] = "Carlito"
        });
    }
}

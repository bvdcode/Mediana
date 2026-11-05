// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Drawing
{
    /// <summary>Reusable font and inline artwork sources. Resolution performs no implicit file or network access.
    /// Callbacks and providers must support concurrent use when options are shared between render operations.</summary>
    public record TextOptions
    {
        public Func<FontRequest, ReadOnlyMemory<byte>>? FontResolver { get; init; }
        public Func<FontRequest, int, ReadOnlyMemory<byte>>? FallbackFontResolver { get; init; }
        public IInlineGraphicProvider? InlineGraphicProvider { get; init; }
        public bool SubstituteMissingFonts { get; init; }
        public IReadOnlyList<string> SubstituteFamilies { get; init; } = ["Canada1500"];
        public int MaximumFontFaces { get; init; } = 64;
        public long MaximumFontBytes { get; init; } = 64L * 1024 * 1024;
        public int MaximumInlineGraphics { get; init; } = 256;
        public int MaximumPathCommands { get; init; } = 2_000_000;
    }
}

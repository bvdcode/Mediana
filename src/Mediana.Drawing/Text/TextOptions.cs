// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Drawing
{
    /// <summary>Reusable font and inline artwork sources. Resolution performs no implicit file or network access.
    /// Callbacks and providers must support concurrent use when options are shared between render operations.</summary>
    public record TextOptions
    {
        /// <summary>Resolves shared parsed font programs without copying or reparsing their storage.</summary>
        public Func<FontRequest, Fonts.FontProgram?>? FontProgramResolver { get; init; }
        public Func<FontRequest, int, Fonts.FontProgram?>? FallbackFontProgramResolver { get; init; }
        public Func<FontRequest, ReadOnlyMemory<byte>>? FontResolver { get; init; }
        public Func<FontRequest, int, ReadOnlyMemory<byte>>? FallbackFontResolver { get; init; }
        public IInlineGraphicProvider? InlineGraphicProvider { get; init; }
        public bool SubstituteMissingFonts { get; init; }
        public IReadOnlyList<string> SubstituteFamilies { get; init; } = ["Canada1500"];
        /// <summary>Bundled families consulted only for missing characters after custom fallback resolvers.
        /// Requires SubstituteMissingFonts. Programs are loaded only when a character needs them.</summary>
        public IReadOnlyList<string> FallbackFamilies { get; init; } = ["Noto Sans Symbols 2"];
        public int MaximumFontFaces { get; init; } = 64;
        public long MaximumFontBytes { get; init; } = 64L * 1024 * 1024;
        public int MaximumInlineGraphics { get; init; } = 256;
        /// <summary>Bounds retained outline commands, prepared contour points and cached coverage pixels independently within one renderer.</summary>
        public int MaximumPathCommands { get; init; } = 2_000_000;
    }
}

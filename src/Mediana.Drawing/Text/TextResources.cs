// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing.Fonts;

namespace Mediana.Drawing
{
    /// <summary>Reusable text resources for any document renderer. System discovery runs only through LoadSystemAsync.</summary>
    public static class TextResources
    {
        public static Task<TextOptions> LoadSystemAsync(TextResourceOptions? options = null, CancellationToken cancellationToken = default) =>
            LoadAsync(SystemFontFiles.Enumerate(), options, cancellationToken);

        public static async Task<TextOptions> LoadAsync(IEnumerable<string> fontFiles, TextResourceOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            options ??= new();
            ArgumentNullException.ThrowIfNull(options.FamilyAliases);
            Dictionary<string, string> aliases = new(options.FamilyAliases, StringComparer.OrdinalIgnoreCase);
            FontCollection fonts = await FontCollection.LoadAsync(fontFiles, options.MaximumFontBytes, options.MaximumFonts, cancellationToken).ConfigureAwait(false);
            return new()
            {
                SubstituteMissingFonts = options.SubstituteMissingFonts,
                FontProgramResolver = Resolve,
                FallbackFontProgramResolver = fonts.ResolveCharacterProgram,
                InlineGraphicProvider = options.InlineGraphicProvider
            };

            FontProgram? Resolve(FontRequest request)
            {
                FontProgram? exact = fonts.ResolveProgram(request);
                if (exact is not null) { return exact; }
                return aliases.TryGetValue(request.Family, out string? family)
                    ? fonts.ResolveProgram(request with { Family = family }) : null;
            }
        }
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Drawing
{
    public partial class TextRenderer
    {
        /// <summary>Returns a common diagnostic when the resolved font family differs from the request.</summary>
        public static string? DescribeFontSubstitution(FontRequest request, Fonts.TrueTypeFont font)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(font);
            string? family = font.ReadFaceInfo()?.Family;
            if (family is null || family.Replace(" ", "", StringComparison.Ordinal).Equals(request.Family.Replace(" ", "", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase))
            { return null; }
            return "Font '" + request.Family + "' is rendered with '" + family + "'. Text layout can change with substituted fonts.";
        }
    }
}

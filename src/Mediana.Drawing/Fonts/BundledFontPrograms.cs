// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using System.Resources;
using System.Collections.Concurrent;

namespace Mediana.Drawing.Fonts
{
    /// <summary>Bundled font programs shared by document and image renderers.</summary>
    public static class BundledFontPrograms
    {
        private static readonly ResourceManager Manager = new(typeof(BundledFontPrograms));
        private static readonly ConcurrentDictionary<string, Lazy<FontProgram>> Programs = new();

        /// <summary>Returns an immutable program parsed once and shared across concurrent renderers.</summary>
        public static FontProgram GetProgram(string family, bool bold = false, bool italic = false) =>
            Programs.GetOrAdd(ResourceName(family, bold, italic), static name =>
                new Lazy<FontProgram>(() => new FontProgram(Read(name)))).Value;

        /// <summary>Returns a read-only OpenType or WOFF program. Storage is shared and must not be mutated.
        /// Noto Sans Symbols 2 provides a single regular face for all style requests.</summary>
        public static ReadOnlyMemory<byte> GetFont(string family, bool bold = false, bool italic = false) =>
            Read(ResourceName(family, bold, italic));

        private static string ResourceName(string family, bool bold, bool italic)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(family);
            if (family == "Noto Sans Symbols 2") { return "NotoSansSymbols2-Regular"; }
            string name = family switch
            {
                "Aileron" => "Aileron",
                "Canada1500" => "Canada1500",
                _ => throw new ArgumentException("The bundled family must be Aileron, Canada1500 or Noto Sans Symbols 2.", nameof(family))
            };
            string variant = (bold, italic) switch
            {
                (false, false) => "Regular",
                (true, false) => "Bold",
                (false, true) => "Italic",
                (true, true) => "BoldItalic"
            };
            return name + "-" + variant;
        }

        internal static byte[] Read(string name) => Manager.GetObject(name, CultureInfo.InvariantCulture) as byte[]
            ?? throw new InvalidOperationException("Bundled font program is missing: " + name);
    }
}

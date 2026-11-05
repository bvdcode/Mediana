// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using System.Resources;
using System.Collections.Concurrent;

namespace Mediana.Drawing.Fonts
{
    /// <summary>Existing bundled CC0 font programs shared by document and image renderers.</summary>
    public static class BundledFontPrograms
    {
        private static readonly ResourceManager Manager = new(typeof(BundledFontPrograms));
        private static readonly ConcurrentDictionary<(string Family, bool Bold, bool Italic), Lazy<FontProgram>> Programs = new();

        /// <summary>Returns an immutable program parsed once and shared across concurrent renderers.</summary>
        public static FontProgram GetProgram(string family, bool bold = false, bool italic = false) =>
            Programs.GetOrAdd((family, bold, italic), static key =>
                new Lazy<FontProgram>(() => new FontProgram(GetFont(key.Family, key.Bold, key.Italic)))).Value;

        /// <summary>Returns a read-only program for Aileron or Canada1500. Storage is shared and must not be mutated.</summary>
        public static ReadOnlyMemory<byte> GetFont(string family, bool bold = false, bool italic = false)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(family);
            string name = family switch
            {
                "Aileron" => "Aileron",
                "Canada1500" => "Canada1500",
                _ => throw new ArgumentException("The bundled family must be Aileron or Canada1500.", nameof(family))
            };
            string variant = (bold, italic) switch
            {
                (false, false) => "Regular",
                (true, false) => "Bold",
                (false, true) => "Italic",
                (true, true) => "BoldItalic"
            };
            return Read(name + "-" + variant);
        }

        internal static byte[] Read(string name) => Manager.GetObject(name, CultureInfo.InvariantCulture) as byte[]
            ?? throw new InvalidOperationException("Bundled font program is missing: " + name);
    }
}

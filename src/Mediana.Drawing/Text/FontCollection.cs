// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing.Fonts;
using System.Buffers.Binary;

namespace Mediana.Drawing
{
    /// <summary>A bounded collection of caller-supplied installed font files. It performs no implicit discovery or network access.
    /// Family names and style attributes come from OpenType tables, rather than filenames. Resolution is read-only and thread-safe.</summary>
    public class FontCollection
    {
        private readonly IReadOnlyList<(TrueTypeFont Font, FontFaceInfo? Info)> _fonts;
        private readonly IReadOnlyDictionary<FontRequest, TrueTypeFont> _faces;
        private FontCollection(IReadOnlyList<(TrueTypeFont Font, FontFaceInfo? Info)> fonts, IReadOnlyDictionary<FontRequest, TrueTypeFont> faces)
        {
            _fonts = fonts;
            _faces = faces;
        }

        /// <summary>Loads bounded static TrueType outline programs. Bitmap-only, variable, CFF and collection formats are excluded.</summary>
        public static async Task<FontCollection> LoadAsync(IEnumerable<string> files, int maximumBytes = 64 * 1024 * 1024,
            int maximumFonts = 256, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(files);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFonts);
            List<(TrueTypeFont Font, FontFaceInfo? Info)> fonts = [];
            Dictionary<FontRequest, TrueTypeFont> faces = [];
            long total = 0;
            foreach (string file in files.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using FileStream source = new(file, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
                long length = source.Length;
                if (length < 12 || length > 32 * 1024 * 1024) { continue; }
                if (length > maximumBytes - total || fonts.Count == maximumFonts) { break; }
                byte[] bytes = new byte[(int)length];
                await source.ReadExactlyAsync(bytes, cancellationToken);
                if (await source.ReadAsync(new byte[1], cancellationToken) != 0) { throw new InvalidDataException("Font changed length during reading."); }
                if (!HasStaticOutlines(bytes)) { continue; }
                TrueTypeFont font = new(bytes);
                FontFaceInfo? faceInfo = font.ReadFaceInfo();
                fonts.Add((font, faceInfo));
                total += font.Program.Length;
                if (faceInfo is FontFaceInfo info)
                {
                    FontRequest request = new(info.Family.ToUpperInvariant(), info.Weight >= 600, info.Italic);
                    faces.TryAdd(request, font);
                }
            }
            return new(fonts.AsReadOnly(), faces);
        }

        /// <summary>Returns a matching family and face, or empty. Attach to TextOptions.FontResolver.</summary>
        public ReadOnlyMemory<byte> Resolve(FontRequest request) =>
            _faces.TryGetValue(request with { Family = request.Family.ToUpperInvariant() }, out TrueTypeFont? font) ? font.Program : ReadOnlyMemory<byte>.Empty;

        /// <summary>Finds an outline glyph for a missing character, preferring the requested face style. Attach to FallbackFontResolver.</summary>
        public ReadOnlyMemory<byte> ResolveCharacter(FontRequest request, int codepoint)
        {
            TrueTypeFont? alternative = null;
            foreach ((TrueTypeFont font, FontFaceInfo? info) in _fonts)
            {
                if (font.Glyph(codepoint) == 0) { continue; }
                alternative ??= font;
                if (info is not null && (info.Weight >= 600) == request.Bold && info.Italic == request.Italic) { return font.Program; }
            }
            return alternative?.Program ?? ReadOnlyMemory<byte>.Empty;
        }

        private static bool HasStaticOutlines(ReadOnlySpan<byte> bytes)
        {
            if (!(bytes[..4].SequenceEqual("OTTO"u8) || BinaryPrimitives.ReadUInt32BigEndian(bytes) == 0x10000)) { return false; }
            int count = BinaryPrimitives.ReadUInt16BigEndian(bytes[4..]);
            if (count > 256 || 12 + count * 16 > bytes.Length) { throw new InvalidDataException("Invalid installed OpenType table directory."); }
            bool outlines = false, dynamicOrCff = false;
            for (int i = 0; i < count; i++)
            {
                ReadOnlySpan<byte> tag = bytes.Slice(12 + i * 16, 4);
                if (tag.SequenceEqual("glyf"u8)) { outlines = true; }
                if (tag.SequenceEqual("fvar"u8) || tag.SequenceEqual("CFF "u8)) { dynamicOrCff = true; }
            }
            return outlines && !dynamicOrCff;
        }
    }
}

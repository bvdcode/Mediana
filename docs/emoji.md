# Color emoji

`Mediana.Drawing.Emoji.Fluent` is an optional MIT-licensed resource module using
Microsoft Fluent Emoji's Flat SVG artwork. It contains 1,595 base sequences in
a 1,496,994-byte archive. The CLI includes this module for Word and SVG previews;
`Mediana.Drawing` and `Mediana.Documents.Word` do not depend on it.

The module uses Mediana's managed SVG renderer and BCL compression. It does not
require an installed emoji font, native drawing library or network connection.
The resources are artwork and Unicode mappings, rather than an OpenType font.

```csharp
using Mediana.Documents.Word;
using Mediana.Drawing;
using Mediana.Drawing.Emoji.Fluent;

using FluentEmojiProvider emoji = new();
WordDocument document = WordReader.Read(encodedDocument);
WordLayout layout = WordRenderer.Layout(document, new()
{
    Text = new() { InlineGraphicProvider = emoji, SubstituteMissingFonts = true }
});
using PixelBuffer pixels = await WordRenderer.RenderAsync(layout, 1,
    cancellationToken: cancellationToken);
```

The provider implements `IInlineGraphicProvider`, a Drawing contract accepting
one complete Unicode text element. Hosts can supply another implementation.
Returned `InlineGraphic` instances expose em-based advance, ascent and descent
and draw directly into a caller-owned `RasterCanvas`. They remain valid after
the provider's cache is cleared or the provider is disposed.

The same `TextOptions` can be passed to `SvgReaderOptions.Text` and
`PdfReaderOptions.Text`. SVG text resolves color artwork through the same provider.
PDF uses the shared source for external font programs; encoded PDF glyphs and
embedded font programs retain their document-defined meaning. See
[shared text resources](text-rendering.md).

## Text and placement

Word and SVG segment adjacent text fragments with BCL `StringInfo` before selecting a
graphic or individual font outlines. ZWJ sequences, keycaps and variation
selectors reach the provider intact, including sequences split between fragments.
The first scalar's formatting sets an inline graphic's size and baseline.
Line wrapping does not split a grapheme cluster.

Supported sequences include individual emoji, available ZWJ professions,
keycaps and the supplied symbolic flags. Text presentation (`U+FE0E`) remains
with the text font; emoji presentation (`U+FE0F`) selects available artwork.
The provider accepts complete matching sequences and does not draw a matching
prefix of an unknown sequence.

Skin-tone variants are omitted from the distributed resource set. Country
flag artwork is absent from this pinned Fluent collection. Unknown sequences
return null and follow the host's existing font and missing-glyph policy.
OpenType color-font tables, general GSUB shaping and bidi layout remain
separate capabilities.

## Storage and ownership

SVG bytes are compressed into independent Brotli blocks of at most 128 KiB.
The importer uses `CompressionLevel.SmallestSize`, Brotli quality 11.
Resolving a new graphic expands one block into pooled temporary storage and
retains an interpreted vector scene. The cache retains at most 128 scenes by
default, configurable through `FluentEmojiOptions.MaximumCachedGraphics`.
Each shared text renderer limits distinct inline graphics to 256 through
`TextOptions.MaximumInlineGraphics`. Word layouts and SVG scenes retain the graphics they use.

Graphics draw onto the page without pre-rasterizing each emoji. SVG opacity
groups may require temporary compositing layers; they share the canvas allocation
budget, have a separate effect workspace bound, and are released after drawing.
The normal page output and supersampling budget still applies. Provider/cache
bounds are not a cap on the entire process or on layouts retained by a caller. Provider resolution and
returned graphics support concurrent callers and independent page renders.

## Verification

Tests resolve and draw every bundled sequence. Additional cases cover text and
emoji selectors, keycaps, ZWJ sequences split across Word runs and SVG spans, wrapping,
concurrent pages, cache eviction, provider disposal, cancellation and memory
limit failures. Temporary compositing storage is released after successful
drawing and failed allocation. Shared rendering cases run through Native AOT.

## Resource provenance

The source is pinned to Microsoft Fluent Emoji commit
`1ffb34c752ecf5d402f04cfb4b392c77f57c54bc`. Original SVG bytes and SHA-256 hashes
are preserved in the archive's manifest.

To rebuild resources from that checkout with a .NET 10 SDK:

```sh
python3 tools/import-fluent-emoji.py <fluent-emoji-checkout> src/Mediana.Drawing.Emoji.Fluent/Assets
```

`--include-skin-tones` includes the additional tone artwork in a custom build.
`--dotnet <executable>` selects an SDK executable explicitly.

References: [Fluent Emoji](https://github.com/microsoft/fluentui-emoji),
[MIT license](https://github.com/microsoft/fluentui-emoji/blob/main/LICENSE),
[Unicode Emoji](https://www.unicode.org/reports/tr51/) and
[Unicode Text Segmentation](https://www.unicode.org/reports/tr29/).

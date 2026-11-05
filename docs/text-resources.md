# Shared text resources

`Mediana.Drawing.TextResources` loads explicitly supplied font files through
`LoadAsync`, or discovers installed fonts through `LoadSystemAsync`. Both methods
apply font-count and byte bounds and return reusable `TextOptions`. Exact faces
take priority over the configured family aliases. Missing-font substitution is
explicitly enabled with `SubstituteMissingFonts`.

```csharp
using FluentEmojiProvider emoji = new();
TextOptions text = await TextResources.LoadSystemAsync(new()
{
    SubstituteMissingFonts = true,
    InlineGraphicProvider = emoji
}, cancellationToken);

MediaEngine engine = new MediaEngineBuilder()
    .UseResources(MediaResourceSet.Empty.With(text))
    .AddImageDecoder(new SvgImageDecoder())
    .AddImageDecoder(new PdfImageDecoder())
    .AddImageDecoder(new OpenXmlImageDecoder([new WordPageDecoder()]))
    .Build();
```

The types above are in `Mediana`, `Mediana.Core`, `Mediana.Drawing`,
`Mediana.Drawing.Emoji.Fluent`, `Mediana.Images.Svg`, `Mediana.Documents.Pdf`,
`Mediana.Documents.OpenXml` and `Mediana.Documents.Word`. Fluent artwork is an
optional package and provider; callers keep it alive through all renders and
dispose it afterward. PDF embedded fonts, encodings and declared glyph advances
retain priority; inline artwork does not replace encoded PDF glyphs.

The same resources reach Word text and images containing SVG text. Direct format
APIs accept the same `TextOptions` in their reader or layout settings. A decode
request can override the engine resource set. Asynchronous resource providers can
be registered with `MediaResourceSet.WithAsync`; providers must support concurrent
calls and own their caching policy. Neither resource registration nor probing
starts font discovery.

Font collections and bundled faces retain immutable `FontProgram` objects.
`TextOptions.FontProgramResolver` and `FallbackFontProgramResolver` share them
without copying bytes or repeating table parsing. Every renderer creates its own
`TrueTypeFont` caches. The byte-based resolver properties accept external font
programs when a caller does not already have parsed resources.

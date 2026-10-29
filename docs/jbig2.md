# Managed JBIG2 decoding

`Mediana.Codecs.Jbig2` decodes standalone sequential/random-access files and
embedded streams with optional globals into packed monochrome pages. It is a
source module with no native or external runtime package dependencies. The code
was adapted from the same author's MIT-licensed Render.NET codec at commit
`03cb502` and builds independently in Mediana.

```csharp
using Mediana.Codecs.Jbig2;

Jbig2Document document = Jbig2Decoder.Decode(await File.ReadAllBytesAsync("scan.jb2"),
    options: new Jbig2DecodeOptions { MaximumPixels = 4_000_000 },
    cancellationToken: cancellationToken);
Jbig2Page page = document.Pages[0];
bool black = page.GetPixel(20, 10);
```

Page association numbers are preserved. Packed samples are MSB-first, one means
black and zero means white. Unused row tail bits are zero. Pages can be processed
individually without first allocating RGBA output. `Jbig2Page.ToVideoFrame` explicitly expands a selected page to an owned opaque sRGB frame with caller-supplied pixel aspect ratio.

The implementation supports arithmetic and MMR generic regions, symbol
dictionaries, text regions, refinement, halftones, standard/custom Huffman tables,
and retained arithmetic contexts. Unknown-length segments/heights, colored
regions, extended templates, necessary unknown extensions and uncompressed T.6
modes are unsupported. This is not a JBIG2 encoder.

Input, pixels, cumulative working allocations, cumulative decoded pixels,
segments, symbols, instances and pages have explicit limits. Their estimates
are not a hard process-memory cap. Cancellation is checked while decoding.
Malformed streams and exceeded limits produce `InvalidDataException`;
unsupported features produce `NotSupportedException`.

The algorithms and numerical tables follow [ITU-T T.88](https://www.itu.int/rec/T-REC-T.88-200002-I/en)
and T.6. The arithmetic decoder is shared with JPEG 2000. Normative Huffman and
fax tables are compiled data and do not require resource discovery or reflection.

Tests compare all packed bytes for 98 original reference files, including
independent PDFBox verification recorded with the fixtures. Additional tests
cover multiple page associations, reference validation, truncated streams,
resource limits and cancellation. This is regression coverage, not full
standard conformance or a throughput measurement.

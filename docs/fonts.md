# TrueType glyph reading

`Mediana.Drawing.Fonts.TrueTypeFont` supplies the TrueType outlines used by SVG
and PDF previews. Each outline reader is restricted to the glyph's own `loca`
extent within the `glyf` table. Headers, contour endpoints, flags, coordinates,
component transforms and instruction data cannot consume an adjacent glyph.
Truncated contour arrays and instruction data are rejected before allocating
the contour list.

A zero-contour glyph may contain only its ten-byte header. Optional instruction
data is read when present. For composite glyphs, `WE_HAVE_INSTRUCTIONS` is
collected from every component; its length and bytes follow the last component.
These cases follow Microsoft's [OpenType `glyf` specification](https://learn.microsoft.com/en-us/typography/opentype/spec/glyf).
The renderer reads unhinted outlines and skips instruction bytes after validating
their extent.

Returned paths are independent copies of cached outlines. The font retains its
own encoded bytes so that changes to the caller's input cannot affect later
glyph reads. Per-glyph byte bounds do not establish a total font-processing
memory or execution-time limit.

Five regression tests cover truncated simple and composite records followed by
a valid adjacent glyph, early allocation rejection, empty headers, instruction
flags on an earlier component, translated contours and independent returned
paths. Three warmed attempts to read a truncated header declaring 32,767
contours allocated 395,280 bytes before this change; the regression guard now
requires less than 24,576 bytes across all three attempts, including exception
handling. This measures that malformed-record case rather than normal rendering
throughput or process memory.

The full suite passes 2,716 managed tests. The complete strict Linux x64 Native
AOT runner also passes. Separate strict Native AOT consumers compared all 8,074
declared glyphs from the eight bundled TrueType files against version 0.1.12:
every path command and coordinate bit remained identical, with no rejected
glyphs. These checks cover the bundled fonts and the specified malformed-record
boundaries; broader font complexity and cumulative resource limits remain open.

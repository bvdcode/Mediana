# OpenType glyph reading

`Mediana.Drawing.Fonts.TrueTypeFont` supplies the TrueType and supported CFF
outlines used by SVG and PDF previews. Each TrueType outline reader is restricted to the glyph's own `loca`
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

## CFF Type 2 outlines

OpenType fonts with a name-keyed CFF version 1 table use a bounded CFF reader.
It validates INDEX offsets/counts, dictionary operands, CharStrings and private
subroutine extents. Type 2 paths support optional widths, stem hints/masks,
local/global subroutines, line/curve operators and the four flex forms. FontMatrix
maps the paths into the font's units per em. Metrics, cmap selection and the
existing outline cache remain common to both formats.

The interpreter limits the operand stack to 48 values, stems to 96, subroutine
nesting to ten calls and byte operations per glyph to 100,000. Returned paths
remain independent. CID-keyed CFF, composite endchar forms, arithmetic operators
and CFF2 are not implemented. Synthetic checks exercise public font dispatch,
curve/flex endpoints, hints, subroutines, malformed indexes, truncation, stack
limits and recursive subroutine rejection. These do not establish complete CFF
font conformance.

The format references are Microsoft's [OpenType CFF specification](https://learn.microsoft.com/en-us/typography/opentype/spec/cff),
Adobe's [CFF format](https://adobe-type-tools.github.io/font-tech-notes/pdfs/5176.CFF.pdf)
and [Type 2 charstrings](https://adobe-type-tools.github.io/font-tech-notes/pdfs/5177.Type2.pdf).

## Font wrappers and pair positioning

The same outline reader reconstructs WOFF 1 tables stored directly or compressed
with zlib. For a TrueType Collection it selects the first face; the API does not
enumerate or select other faces. These code paths do not establish complete WOFF
or TTC/OTC conformance. Dedicated wrapper and multiple-face fixture coverage
remains open. They are font resources for rendering, not standalone image inputs
to the CLI.

`TrueTypeFont.Kerning` reads supported GPOS `kern` pair lookups and legacy `kern`
pairs; SVG text applies the resulting horizontal adjustment. This is a subset of
OpenType positioning. GSUB substitution, script shaping and bidirectional text
layout are not implemented by this pair API. These distinctions are tracked in
the [format checklist](formats/README.md#fonts-and-text-presentation-resources).

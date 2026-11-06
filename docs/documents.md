# Documents

Document modules expose reading and rendering independently. XLS and XLSX share value contracts; their file parsers are separate. No Office installation or intermediate PDF is used for PowerPoint rasterization.

## Presentation previews

```csharp
using Mediana.Documents.Presentation;
using Mediana.Drawing;

byte[] encoded = await File.ReadAllBytesAsync("presentation.pptx", token);
using PresentationDocument presentation = PresentationReader.Open(encoded, cancellationToken: token);
TextOptions text = await TextResources.LoadSystemAsync(new() { SubstituteMissingFonts = true }, token);
using PixelBuffer slide = await PresentationRenderer.RenderAsync(presentation, 0,
    new() { MaximumWidth = 1200, MaximumHeight = 1200, Text = text }, token);
```

Slide indexes are zero based. `Slides`, `Width` and `Height` describe the presentation without rendering. Dimensions use points. `PresentationPageDecoder` registers page decoding with `OpenXmlImageDecoder`; the CLI supports `mediana -i presentation.pptx preview.png --page 1`.

The renderer reads the selected slide, its layout, master and theme. It uses Drawing for shapes, text and inline artwork, and the shared image registry for embedded pictures. Registering a decoder through `Images` also makes it available to the presentation. Pictures receive the same allocation budget and sRGB preparation as other documents. `TextResources` and `TextOptions` are shared with Word, SVG and PDF.

Supported drawing elements include groups, offsets, rotation, reflection, inherited placeholders, solid and linear gradient fills, solid outlines, common preset shapes, numeric custom paths, cropped/stretched pictures and horizontal paragraph text. Normal text autofit measures prepared glyphs before drawing; horizontal and vertical overflow follow the stored body properties. Chart/SmartArt frames, theme-styled tables, vertical text and unsupported geometry report their feature explicitly. Animation playback and automatic bullet numbering remain open.

## Sequential spreadsheets

```csharp
using Mediana.Documents.Xlsx;
using Mediana.Documents.Spreadsheets;

using XlsxWorkbook workbook = XlsxReader.Open(encoded, cancellationToken: token);
foreach (SpreadsheetSheet sheet in workbook.Sheets)
{
    if (sheet.Kind != SpreadsheetSheetKind.Worksheet) { continue; }
    foreach (SpreadsheetRow row in workbook.ReadRows(sheet.Index, token))
    {
        foreach (SpreadsheetCell cell in row.Cells)
        {
            SpreadsheetValue value = cell.Value;
        }
    }
}
```

For binary Excel files use `XlsReader.Open`, which reads CFB sector chains and BIFF8 records. It does not route through XLSX or Open XML. Other BIFF versions and encrypted workbooks report unsupported input.

Rows and cells retain their zero-based source coordinates. Missing rows/cells are not synthesized. Values distinguish blank, text, number, Boolean, error and date/time. Number formats determine numeric date values; the original serial and the workbook's 1900/1904 date system remain available. The fictitious 1900-02-29 has no `DateTime` value.

`SpreadsheetCell.Formula` preserves a formula's source and whether a cached result exists. XLS retains binary tokens and auxiliary data; XLSX retains expression text, shared/array metadata and reference ranges. Formula evaluation and expansion of shared formulas are not performed. A missing cache remains blank with `HasCachedValue == false`; a cached empty string remains text.

## Unicode text blocks

`IDocumentTextSource.ReadText` provides the same lazy contract for PDF, DOCX, PPTX, XLS and XLSX:

```csharp
using Mediana.Documents;
using Mediana.Documents.Word;

IDocumentTextSource document = new WordTextReader(encoded);
foreach (DocumentTextBlock block in document.ReadText(
    new() { StartPartIndex = 0, PartCount = 1 }, token))
{
    string text = block.Text;
    IReadOnlyList<DocumentTextPathSegment> path = block.Path;
    if (EnoughText(text)) { break; }
}
```

`Path` describes structural ancestors and the current block, including table rows and cells. `PartIndex` identifies the PDF page, DOCX section, presentation slide or worksheet. `PartName` identifies an XML part or sheet when available. A logical PDF paragraph may emit multiple fragments with the same path. Paragraph text preserves stored Unicode, tabs and line breaks; generated field results, list numbers or formula values are not recomputed.

DOCX text follows body order, excluding deleted/moved-away content and field instructions. PPTX follows stored slide shape order. Neither requires fonts, pagination or embedded-image decoding. Spreadsheet text follows sheet, row and cell order.

`PdfDocument.ReadText` uses ToUnicode maps, simple font encodings and ActualText without opening font programs or rasterizing images. Logical tags supply `DocumentTextStructure.Declared`. Untagged lines use geometric ordering and are marked `Reconstructed`; this does not claim semantic reconstruction of columns, headings or tables. PDF inline-image content streams and composite encodings other than Identity-H remain open.

Enumerators release their active part readers when disposed, including on `break`, `Take` or cancellation. A workbook/presentation borrows its input until disposed. A PDF opened with `PdfReader.Open` also borrows its memory; `ReadAsync` owns the bytes read from the caller's stream. Do not mutate borrowed data or dispose its document while enumerating.

Limits reject excessive output instead of truncating it. Use `StartPartIndex`/`PartCount` for part selection, and normal enumeration control for early completion. `DocumentTextOptions.WarningHandler` receives decoding recovery diagnostics. Parsing limits are configurable in the format-specific reader options.

## Validation

Document tests cover independent CFB and OPC parsers, sparse coordinates, typed values, cached formulas, shared-string continuation, Unicode mappings, declared/reconstructed reading order, cancellation and early disposal. Presentation checks compare reference pixels, inherited placeholders, automatic text sizing, per-axis overflow, shared image decoding and single-buffer publication. The same public-reader checks run in the Native AOT executable.

Text extraction passed on 18 DOCX and 14 PPTX documents and two independent BIFF8 workbooks. First-slide previews succeeded for 12 of those presentations after enabling common ICC matrix/TRC conversion. Two stop at a picture fill (`blipFill`) and conflicting JFIF/Exif chroma-position declarations. All six distinct embedded RGB profiles matched LittleCMS conversion within one 8-bit RGB level; alpha matched exactly. Large-file checks use explicitly increased package limits; default limits remain unchanged.

## Specifications

- [Microsoft PresentationML structure](https://learn.microsoft.com/en-us/office/open-xml/presentation/structure-of-a-presentationml-document)
- [Microsoft SpreadsheetML sheets](https://learn.microsoft.com/en-us/office/open-xml/spreadsheet/working-with-sheets)
- [MS-CFB](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-cfb/53989ce4-7b05-4f8d-829b-d08d6148375b)
- [MS-XLS](https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-xls/cd03cb5f-ca02-4934-a391-bb674cb8aa06)
- [PDF 1.7 reference, sections 5.9 and 10.6–10.8](https://opensource.adobe.com/dc-acrobat-sdk-docs/pdfstandards/pdfreference1.7old.pdf)

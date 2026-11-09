# HDR previews

`SrgbConverter` and `SrgbPreview` support explicit PQ/HLG-to-sRGB conversion through
`HdrToneMappingOptions`. `MediaEngine.ExportPreviewAsync`, `ExportImageAsync` and
`ExportAnimationAsync` expose the same operation through `VisualExportOptions.ToneMapping`.
Without this option, HDR-to-sRGB conversion remains strict. The native decoder
retains the original integer precision and source color description.

```csharp
VisualExportOptions options = new()
{
    ColorResolver = PreviewColorPolicy.Resolve,
    ToneMapping = new HdrToneMappingOptions
    {
        SourcePeakLuminance = 1000,
        TargetPeakLuminance = 100
    },
    UnspecifiedSampleAspectRatio = SampleAspectRatio.Square,
    WarningHandler = Console.Error.WriteLine
};
await engine.ExportPreviewAsync(source, output, new PngImageEncoder(), allocator, options, cancellationToken);
```

Luminance is expressed in cd/m². The source peak is an explicit display assumption,
not a measurement of the content. The current operation does not analyze frames,
select peaks from mastering metadata, or apply dynamic HDR metadata. Hable's
highlight curve is normalized at the source-to-target peak ratio; the maximum RGB
component determines one common gain, retaining component ratios before sRGB gamut
clipping. Tone mapping is a rendering choice and does not have a unique lossless
SDR result.

PQ uses the absolute BT.2100 EOTF. HLG uses the inverse OETF and luminance-dependent
OOTF for BT.2020 primaries with a zero-black reference display; system gamma follows
the chosen source peak. Linear RGB is converted to BT.709 primaries before tone
mapping and sRGB encoding. Straight alpha is preserved. Embedded ICC profiles retain
precedence over the CICP color resolver and follow the existing ICC transform.

PQ and HLG transfer tables contain 4097 double-precision values each, initialized
once when first needed and interpolated across calls. Geometry and color conversion
share the existing preview pass: rotated or reduced previews allocate only their
output pixels, without an intermediate full-resolution RGB frame. Cancellation,
source ownership and the allocator budget follow the ordinary conversion contract.

The CLI selects this presentation policy with a 1000 cd/m² source peak and
100 cd/m² target peak, reporting the interpretation as a warning. Missing SAR is
interpreted as 1:1, also with a warning. Declared SAR and color characteristics have
priority; neither operation modifies source metadata. The same rules apply to all
registered visual input formats.

Independent gray, primary and mixed-color ramps for PQ and HLG match FFmpeg/zimg's
corresponding Hable conversion within one 8-bit channel value. Tests also cover
the 100/1000/10000 cd/m² PQ reference points, HLG endpoints, source metadata, alpha,
ICC precedence, export warnings, exact preview allocation and rotated output.
See the [reference corpus](../tests/Mediana.Tests/Data/Preview/README.md#hdr-tone-mapping-references).

Transfer definitions: [ITU-R BT.2100](https://www.itu.int/rec/R-REC-BT.2100),
Tables 4 and 5. The operator is also available in
[FFmpeg's tonemap filter](https://ffmpeg.org/ffmpeg-filters.html#tonemap).

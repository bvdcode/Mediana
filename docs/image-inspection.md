# Image metadata and encoded previews

Image metadata can be registered independently of pixel decoding:

```csharp
MediaEngine engine = new MediaEngineBuilder()
    .AddImageInspector(new PcxMetadataReader())
    .AddImageInspector(new PsdMetadataReader())
    .AddImageInspector(new TiffImageMetadataReader())
    .AddImageInspector(new RafMetadataReader())
    .Build();

ImageInfo info = await engine.InspectImageAsync(encoded, cancellationToken);
IReadOnlyList<EncodedImagePreview> previews =
    await engine.ReadImagePreviewsAsync(encoded, cancellationToken);
```

The readers are supplied by `Mediana.Images.Metadata`. They implement
`IImageInspector` and `IImagePreviewReader`; a custom inspector can participate
through the same contracts. `MediaEngine.ProbeAsync` uses these readers for
byte-source input, including nonseekable streams. Registration snapshots are
immutable. Explicit inspectors take precedence over decoder-supplied metadata;
multiple matching inspectors are rejected. A parsing failure is propagated.
Existing image decoders continue to supply metadata through `IImageInfoReader`.

`ImageInfo` owns its metadata values. An `EncodedImagePreview` borrows the exact
original compressed bytes: it neither decodes nor re-encodes the preview.
The caller must keep the supplied memory owner alive and unmodified while using
preview slices. Copy a slice explicitly when independent storage is needed.
Preview dimensions describe that preview; they do not replace the parent image's
dimensions. `preview/0`, `preview/1`, etc. metadata scopes expose matching indexes,
format, dimensions, byte length and the preview's own metadata.

Inspection of supplied memory does not copy the encoded input. Byte-source image
probing currently buffers the encoded file within `MediaOpenOptions` input and
allocation limits; it does not decode image samples. Successful inspection does
not establish that compressed raster or layer payloads are valid.

## PCX

The inspector reads the 128-byte header: dimensions and origin, version,
compression declaration, plane count and bit depth, line stride, resolution,
screen size and palette interpretation. It retains the header palette when
present and a trailing 256-color palette when declared by the file structure.
Header bounds and stride are validated without traversing RLE samples.
PCX has no standard EXIF/GPS or encoded JPEG preview fields. Pixel decoding is
not supplied by this metadata module.

## PSD and PSB

The inspector reads PSD/PSB version, stored canvas dimensions, channel count,
bit depth, color mode, compression method and bounded image-resource blocks.
EXIF resources reuse the shared TIFF/EXIF/GPS reader. ICC bytes and XMP packets
retain their respective metadata namespaces. Resource names remain original
Pascal-string bytes. Unknown resources retain their identifier and byte length.
Layer data is skipped using its declared extent; merged pixels are not decoded.

JPEG thumbnail resources expose their original bytes and validated dimensions.
Photoshop 4 thumbnails retain a `BGR` component-order property; later thumbnails
retain `RGB`. Reading original bytes does not correct that source convention.
Raw thumbnail resources expose their size and format declaration but are not
reported as encoded JPEGs. EXIF capture fields and orientation remain in their
native metadata scope; they do not change Photoshop's stored canvas geometry.

PCX fixtures were encoded with Pillow 12.3. The procedural PSD fixture's complete
merged RGB pixels match Pillow decoding; its JPEG thumbnail also comes from
Pillow. The PSB fixture exercises the corresponding large-document section
lengths. Tests cover malformed metadata boundaries, preview byte identity,
owned metadata, borrowed previews, cancellation, registry snapshots and source
cursor preservation. A Native AOT run exercises PCX, PSD and PSB inspection and
compares the complete original JPEG preview.

References: [Adobe Photoshop file formats](https://www.adobe.com/devnet-apps/photoshop/fileformatashtml/),
[ZSoft PCX technical reference](https://www.fileformat.info/format/pcx/spec/index.htm).

## TIFF-based RAW and RAF

`TiffImageMetadataReader` shares the existing TIFF/EXIF/GPS parser. It accepts
classic TIFF, BigTIFF and Olympus ORF and Panasonic RW2 headers. Camera family
identification covers DNG, CR2, NEF/NRW, ARW, ORF, RW2, PEF and SRW.
For Nikon CFA input, a JPEG thumbnail in IFD0 identifies NRW separately from NEF.

`raw.sensor_width` and `raw.sensor_height` retain the stored RAW image dimensions,
including borders where the format declares them. The primary `ImageInfo` applies
the declared orientation. Preview sizes are separate; an embedded JPEG cannot
replace a missing sensor-size declaration. EXIF/GPS capture fields retain their
native names, types and fractions. Camera MakerNotes remain opaque metadata,
except for documented preview directories and offset conventions.

Original JPEG previews are read from TIFF JPEG offset/length pairs, single
JPEG-compressed RGB/YCbCr strips, Panasonic JPEG fields, and known Nikon, Olympus,
Pentax and Samsung MakerNote layouts. The CR2 header identifies the sensor IFD;
a separate preview IFD may contain either DCT or predictive Huffman JPEG.
The original preview bytes are borrowed, including previews outside a MakerNote's
own byte extent when the vendor's offset origin references the containing file.
RAW extraction validates each declared JPEG through its EOI and returns exactly
those bytes. It can select another valid candidate when an extent or JPEG header
is damaged. If every declared candidate fails, extraction reports the read error.
A RAW without a declared embedded JPEG returns null.

For preview extraction the TIFF reader reads only fields that locate and
interpret the image. Full metadata inspection also validates capture strings and
other TIFF values. For unmarked three-component JPEG previews, the preview
IFD's RGB or YCbCr declaration supplies color interpretation. Where absent,
component identifiers R/G/B or 1/2/3 identify camera RGB or YCbCr respectively.

`RafMetadataReader` reads the Fujifilm CCD-RAW header and proprietary metadata
directory. It retains full sensor dimensions, crop origin and dimensions, aspect
ratio, camera/version fields and native directory values. Capture EXIF and
orientation come from the embedded JPEG; its original bytes are available as a
preview. M-RAW and separate secondary raw-image directories are not interpreted.

Procedural ORF/RW2/NRW/PEF/SRW fixtures cover both byte orders, oriented sensor
sizes, EXIF capture fields and distinct preview dimensions. ExifTool independently
identifies each family and reproduces the complete JPEG bytes from Nikon, Olympus
and Pentax MakerNotes. A Fujifilm RAF reference also matches ExifTool's full sensor
size of 4352×1444. Tests exercise invalid offsets, corrupt preview structures and
cancellation. Native AOT inspection passes for all six newly added RAW families.

Format references: [ExifTool TIFF/Exif](https://github.com/exiftool/exiftool/blob/master/lib/Image/ExifTool/Exif.pm),
[Panasonic RAW](https://github.com/exiftool/exiftool/blob/master/lib/Image/ExifTool/PanasonicRaw.pm),
[MakerNote layouts](https://github.com/exiftool/exiftool/blob/master/lib/Image/ExifTool/MakerNotes.pm),
[Fujifilm RAF](https://github.com/exiftool/exiftool/blob/master/lib/Image/ExifTool/FujiFilm.pm).

## AVIF

`AvifImageInspector`, in `Mediana.Images.Heif`, uses the shared HEIF item and
property parser. It reads the primary image's stored and transformed dimensions,
AV1 profile/level/bit depth and chroma sampling, CICP/ICC, EXIF/GPS, grid layout,
item types and thumbnail/auxiliary relationships. It performs no AV1 pixel decode
and does not apply the configured pixel-decoding limit. Input and item limits
remain caller-configurable. The item-based primary presentation is inspected;
sequence track timing and layered-image selection are not interpreted.

Thumbnail extraction returns original `av1` OBU item payloads with their `av1C`
configuration in `av1.configuration` metadata and their presentation transforms.
These are elementary coded image items, not standalone `.avif` files. A single
extent is borrowed; multiple extents are assembled without re-encoding. Derived
thumbnail item types remain visible through item relationships but are not
exported as elementary AV1 thumbnails.

Pillow/libavif independently encoded the 37×29 reference image. A procedural
container with independently encoded AV1 items verifies rotation, separate 7×5
thumbnail, EXIF and original payload access. ExifTool agrees with the stored
size, rotation and capture fields. Tests reject malformed configuration records,
truncated boxes and invalid file-type headers; cancellation is checked. Existing
HEIF decoding and metadata tests also pass after sharing the AVIF property path.

References: [AVIF specification](https://aomediacodec.github.io/av1-avif/),
[AV1 ISO media binding](https://aomediacodec.github.io/av1-isobmff/).

## Canon CR3

`Cr3MetadataReader` reads Canon's ISO BMFF metadata UUID, reusing the shared
TIFF reader for CMT1 (IFD0), CMT2 (Exif), CMT3 (MakerNotes) and CMT4 (GPS).
Sensor dimensions and borders come from Canon SensorInfo. CMT1's rendered image
size remains a separate declaration; it does not replace sensor dimensions.
EXIF orientation determines the primary presentation dimensions.

JPEG previews are returned unchanged from THMB and PRVW boxes and indexed JPEG
samples in CRAW tracks. The latter reuse the MP4 sample/chunk index, including
64-bit offsets, bounds against media-data extents, and sample count validation.
The CRX sensor stream is not decoded. This metadata module references the managed
MP4 container module to share its index implementation. CR3 burst-specific timed
capture updates and HEVC preview extraction are not interpreted.

The procedural CR3 fixture distinguishes sensor, rendered-image and preview
geometry. It contains separate THMB, PRVW and indexed JPEG samples, with ExifTool
confirming sensor dimensions, orientation and GPS fields. Tests compare all
original JPEG bytes and reject corrupt preview lengths and sample extents.
The bounded ISO BMFF box reader is shared by CR3, HEIF and in-memory MP4 tables.

References: [Canon CR3 structure](https://github.com/lclevy/canon_cr3/blob/master/readme.md),
[ExifTool Canon metadata](https://github.com/exiftool/exiftool/blob/master/lib/Image/ExifTool/Canon.pm).

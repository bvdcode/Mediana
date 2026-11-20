# Image metadata and encoded previews

Image metadata can be registered independently of pixel decoding:

```csharp
MediaEngine engine = new MediaEngineBuilder()
    .AddImageInspector(new PcxMetadataReader())
    .AddImageInspector(new PsdMetadataReader())
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

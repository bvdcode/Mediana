# Raster decoding and metadata

All raster decoders implement `IImageDecoder` and `IImageInfoReader`. Recognition
uses file signatures. The CLI and `ImageResources.DefaultDecoders` register the
same decoder implementations; images embedded in SVG and documents use the
supplied `ImageDecodeContext`.

| Module | Decoded representations |
| --- | --- |
| `Mediana.Codecs.Bmp` | BMP palettes at 1/4/8 bits, RGB16/24/32, contiguous bitfields, RLE4/RLE8, top-down rows, OS/2 core and Windows V2–V5 headers; ICO/CUR PNG and DIB entries, alpha and AND masks |
| `Mediana.Codecs.Gif` | GIF87a/89a, global/local palettes, GIF LZW, four-pass interlace, transparent indices, disposal 0–3, frame delays and loop metadata |
| `Mediana.Codecs.Tiff` | Classic TIFF and BigTIFF, both byte orders, strips/tiles, chunky/separate unsigned 1/2/4/8/16-bit samples, gray/RGB/palette/CMYK, straight/associated alpha and orientations 1–8 |
| `Mediana.Codecs.Webp` | VP8 keyframes and VP8L, raw/compressed filtered alpha, extended RIFF, animation rectangles, source-over/replacement, disposal, timing, loop/background metadata |

TIFF compression includes uncompressed samples, PackBits, TIFF LZW, zlib/Deflate,
CCITT modified Huffman, Group 3/4 and JPEG compression 7 with shared JPEG tables.
JPEG YCbCr blocks use the TIFF photometric declaration. Non-JPEG subsampled
YCbCr, legacy JPEG compression 6, signed/floating-point samples and CCITT
uncompressed extensions remain open. GIF plain-text rendering remains open.
The pixel output of these decoders is straight RGBA8. Sixteen-bit TIFF samples
are rounded to eight bits after associated-alpha normalization.

## Selection and ownership

```csharp
MediaEngine engine = new MediaEngineBuilder()
    .AddImageDecoder(new BmpImageDecoder())
    .AddImageDecoder(new IcoImageDecoder())
    .AddImageDecoder(new GifImageDecoder())
    .AddImageDecoder(new TiffImageDecoder())
    .AddImageDecoder(new WebpImageDecoder())
    .Build();

using VideoFrame page = await engine.DecodeImageAsync(encoded, allocator,
    new ImageDecodeRequest { PageIndex = 1 }, cancellationToken: cancellationToken);
```

`PageIndex` is zero-based and selects TIFF pages or ICO/CUR directory entries.
GIF and WebP implement `IImageSequenceDecoder`: enumeration produces one cycle
with the declared delays, including zero delays. `Time` selects a frame after
compositing all preceding frames. Each returned frame owns its pixels and remains
valid when enumeration advances or the decoder is disposed. Callers dispose each
frame; early enumeration termination releases canvas/workspace storage.

Encoded memory is borrowed through the call or enumeration. The allocator
accounts for output storage, animation canvases/backups and decoded blocks.
TIFF expands one strip/tile at a time. A still WebP/GIF transfers its final canvas
without a second complete RGBA copy. Objects, input storage and BCL compression
internals are outside the live-buffer budget. Cancellation is checked during
parsing, image rows, compression and canvas operations. `MaximumPixels` is an
optional caller policy; these decoders impose no default megapixel cap.

## Metadata without sample decoding

`WebpInfoReader.ReadInfoAsync` and `WebpImageDecoder.ReadInfoAsync` return canvas
geometry, codec/frame data, animation rectangles/timing and original ICC/XMP
bytes. EXIF/GPS is parsed by `ExifMetadataReader`. Image samples need not be
valid for metadata inspection to succeed. ICC profiles remain attached to the
frame; applying arbitrary ICC profiles requires color-management support.

`TiffMetadataReader.Read` inspects TIFF and these initial TIFF-based RAW families:

- Adobe DNG, identified by `DNGVersion`.
- Canon CR2, identified by its CR2 header and sensor directory pointer.
- Nikon NEF and Sony ARW, identified by camera make and CFA sensor directories.

The selected RAW directory supplies sensor dimensions instead of the thumbnail.
The metadata tree preserves all recognized directory fields, SubIFDs, EXIF/GPS,
CFA layout, compression, crop/active-area fields, DNG calibration values and
opaque MakerNotes. DNG's declared `0/0` unknown rational values remain `0/0`.
Vendor MakerNote interpretation, RAW sample decoding/demosaicing and other RAW
container families (CR3, RAF, ORF and RW2) are separate capabilities.

```text
mediana probe picture.webp
mediana -i camera.dng
mediana -i picture.bmp preview.png
mediana -i icon.ico preview.png --page 2
mediana -i scan.tif preview.png --page 2
mediana -i animation.gif frame.png --time-ms 120
mediana -i animation.webp output.webp --animate
```

## Verification

The procedural corpus covers 100 BMP/ICO, TIFF and WebP cases, plus GIF frame
references. Independent pixels come from Pillow/libtiff, FFmpeg and libwebp;
TIFF 16-bit associated-alpha fixtures also verify sample normalization before
quantization. Lossless output matches every RGBA byte. GIF comparisons disregard
RGB hidden by zero alpha. JPEG and VP8 comparisons allow up to four levels of
RGB difference for inverse transform/color-conversion rounding; alpha is exact.

Managed tests check page/time selection, retained frames, metadata-only inputs,
400 truncated/mutated streams, cancellation, early stop and allocator release.
The raster corpus also runs in Native AOT. Standard EXIF/GPS and JBIG2 tests pass
after sharing TIFF directory parsing and CCITT tables. CLI tests verify signature
recognition under an unrelated extension, probing, PNG conversion and RAW metadata.
Broad CPU and whole-process peak-memory comparisons remain open.

Specifications: [GIF89a](https://www.w3.org/Graphics/GIF/spec-gif89a.txt),
[TIFF 6.0](https://www.itu.int/itudoc/itu-t/com16/tiff-fx/docs/tiff6.pdf),
[BigTIFF](https://libtiff.gitlab.io/libtiff/specification/bigtiff.html),
[Windows bitmap structures](https://learn.microsoft.com/en-us/windows/win32/gdi/bitmap-structures),
[WebP RIFF](https://developers.google.com/speed/webp/docs/riff_container),
[VP8L](https://developers.google.com/speed/webp/docs/webp_lossless_bitstream_specification),
[VP8](https://www.rfc-editor.org/rfc/rfc6386),
[DNG 1.6](https://helpx.adobe.com/content/dam/help/en/photoshop/pdf/dng_spec_1_6_0_0.pdf).

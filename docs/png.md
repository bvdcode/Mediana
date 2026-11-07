# PNG images

`Mediana.Codecs.Png` encodes straight RGBA32 sRGB frames as non-interlaced,
8-bit PNG files. It writes sRGB and sample aspect ratio metadata and uses BCL
zlib compression. Input must declare the color interpretation and sample aspect
ratio. Padded input rows are supported; padding bytes are excluded from output.

## Image decoding

`PngImageDecoder` implements `IImageDecoder` and `IImageInfoReader`. Register it
with `MediaEngineBuilder.AddImageDecoder`. The CLI recognizes PNG input by its
signature for `probe`, `-i input` and `-i input output.png`, including renamed files.

Static grayscale, RGB, indexed color, grayscale/alpha and RGBA images support
all standard bit depths: 1/2/4/8/16 as permitted by each color type. Both sequential
rows and the seven Adam7 passes support all five filters. Palette transparency
and grayscale/RGB transparency keys preserve straight alpha, including RGB
components under fully transparent pixels. Transparency keys are compared at
the original sample depth. The output is straight RGBA32; samples are scaled to
8 bits with nearest rounding according to [PNG section 13.12](https://www.w3.org/TR/png-3/#13Sample-depth-rescaling).
This operation changes sample precision for 16-bit inputs; it does not perform
a color conversion or alpha compositing.

The decoder validates every chunk CRC, mandatory chunks, supported singleton
ordering and consecutive IDAT data. Chunk boundaries can split any part of the
zlib stream, including its header and checksum. A truncated zlib stream or an
incorrect scanline length is rejected. Unknown critical chunks are unsupported;
unknown ancillary chunks are skipped. Unused trailing bytes in the final IDAT
are ignored as prescribed by PNG section 11.2.3. APNG remains unsupported.

The primary Exif Orientation tag in an eXIf chunk supports all eight rotations
and reflections, in either TIFF byte order. Both probing and decoding report the
oriented dimensions. The original Exif tree and `png.stored_dimensions` retain
the source declarations. Orientation is applied directly while expanding each
scanline, including Adam7 passes and reduced decoding, without allocating a
second image. Axis swaps invert the sample aspect ratio. Invalid orientation
values, types, counts or truncated TIFF data are rejected before pixel allocation.
The eXIf layout follows [PNG section 11.3.4.5](https://www.w3.org/TR/png-3/#eXIf).

`PngDecodeOptions` bounds encoded bytes, pixel count and chunk count before
decoding. Defaults are 64 MiB, 16,777,216 pixels and 100,000 chunks. Encoded bytes
are borrowed during the decoder call. The supplied allocator accounts for one
output array and two scanlines; there is no concatenated IDAT buffer or complete
filtered-image buffer. BCL zlib workspace and bounded metadata objects are outside
that budget. At 2560×2560 RGBA8, output storage is 26,214,400 bytes and the two
scanlines require another 20,480 bytes. After completion, only the independent
output frame remains allocated. Cancellation and failures release working storage.

The pHYs ratio is retained. Without pHYs, PNG specifies square pixels; a present
zero density leaves the aspect ratio unknown. Color declarations follow the
PNG precedence: cICP, embedded ICC, sRGB, then gAMA/cHRM. Original CICP codes are
retained through `CicpColorDescription`. Standalone gamma/chromaticities and ICC
presence use `PngColorDescription`; ICC profiles are not decompressed or retained.
Color conversion requiring these descriptions is unsupported. Untagged images
retain unspecified primaries and transfer. The CLI applies its explicit RGB
preview policy to missing labels and reports the assumption once; it does not
replace an unsupported declared profile with that interpretation.

## Borrowed encoded image input

The common image API accepts an existing `ReadOnlyMemory<byte>` without copying
it into another complete encoded-input buffer. For example, an extracted PNG
cover can be decoded after its container has closed:

```csharp
using MediaBuffer picture = await container.ReadPictureAsync(0, cancellationToken);
await container.DisposeAsync();
using VideoFrame image = await engine.DecodeImageAsync(picture.Memory, allocator,
    cancellationToken: cancellationToken);
```

Register the appropriate container reader and `PngImageDecoder` on `engine`.
Matroska attached files use `ReadAttachmentAsync` instead of `ReadPictureAsync`.
The caller selects the picture or attachment; the decoder does not select a
preferred cover, retrieve external references or choose a destination path.
Keep the supplied memory alive and unmodified until decoding completes. Its
existing lease retains its original complete backing allocation, which can
include surrounding container metadata. Disposing that lease after decoding
does not invalidate the returned frame. Input byte/probe limits, registration
ambiguity, image request validation and late cancellation match the byte-source
operation. Byte-source input still accounts for encoded input and probe storage
through its allocator.

The complete independent PNG corpus contains 36 images, covering all standard
color/depth pairs, both interlace methods, every filter, odd packed widths, empty
passes, split IDAT headers/checksums and transparency. Real PNG pictures from
MP3, FLAC and M4A, and a Matroska attachment, also match all independent RGBA
pixels after container disposal. Shared checks exercise these paths in managed
code and Native AOT, including exact working budgets and release on failure.

## Stream output

```csharp
using Mediana.Codecs.Png;
using Mediana.Core.Memory;

MediaBufferAllocator workingStorage = new(64 * 1024 + 12);
await using FileStream output = new("preview.png", FileMode.CreateNew,
    FileAccess.Write, FileShare.None, bufferSize: 64 * 1024,
    options: FileOptions.Asynchronous);
long bytes = await PngEncoder.EncodeAsync(frame, output, workingStorage, cancellationToken);
```

The operation retains a lease on the input pixels until completion. It reserves
64 KiB plus 12 bytes through the supplied allocator, including PNG chunk framing.
BCL compression workspace, objects and destination buffering have their own
lifetimes and are outside that allocation budget. Neither a complete compressed
image array nor another full RGBA array is created by this operation.

The destination may be non-seekable. All destination writes are asynchronous;
the encoder neither flushes nor closes it. The caller serializes access. The
return value counts bytes written by this operation, independently of the
destination's position. Invalid input, an already cancelled token or insufficient
working storage is rejected before the first write. A later error or cancellation
can leave partial output; discard it. The CLI writes to a temporary file and
publishes the completed file through its existing output transaction.

One zlib datastream spans consecutive IDAT chunks, each carrying at most 64 KiB.
Chunk boundaries do not reset compression, force row boundaries or flush the
compressor. This follows [PNG third edition, section 10.2](https://www.w3.org/TR/png-3/#10CompressionFSL).

## Owned buffer output

`PngEncoder.Encode(frame, allocator, cancellationToken)` returns an independently
owned `MediaBuffer`. The caller disposes it. Its allocation budget counts the
complete reserved array, including unused compression capacity. At 2560×2560
RGBA pixels, that reservation is 29,903,822 bytes; slicing the returned buffer
does not reduce the retained allocation. Use this operation when an owned PNG
buffer is required, and the stream operation when writing to a file or response.

## Verification

Shared managed and Linux Native AOT checks cover exact compressed payloads and
visible pixels, padded rows, non-square sample aspect ratio, wide scanlines,
multiple IDAT chunks, every chunk CRC, asynchronous-only non-seekable output and
source ownership during a deliberately pending write. They also cover early
budget rejection, cancellation and write failures at the header, intermediate
data, final data and IEND, preserving the original error without further writes
or retained working storage.

A serial allocation regression test warms up the encoder and then measures three
complete 1000×500 encodes to a discard stream. The permitted allocation is less
than one quarter of the RGBA image size per operation, rejecting a full pixel
clone or a complete encoded-capacity array even if it bypasses the media budget.

Three alternating pairs of fresh Linux x64 Native AOT CLI processes, pinned to
one CPU core, produced these medians for complete synthetic conversions:

| Input | CPU before / stream | Peak RSS before / stream |
| --- | ---: | ---: |
| PDF RGB image, 2560×2560, incompressible samples | 1.0904 / 1.1178 s | 157408 / 136640 KiB |
| PDF RGB image, 2560×2560, constant samples | 0.4788 / 0.4642 s | 101888 / 101928 KiB |
| SVG solid rectangle, 1280×1280 | 0.3708 / 0.3690 s | 51840 / 51584 KiB |

All 18 complete outputs passed every chunk CRC and every source RGBA component.
Six independent full PNG decodes, one per input and implementation, also matched
the source pixels exactly. IDAT framing changes file bytes when the compressed
payload spans multiple chunks; it does not change the pixels.

A separate Native AOT allocation measurement used a 2560×2560 constant RGBA
frame, two warmups and five complete encodes to a discard stream per process.
Across three fresh processes per operation, allocation was 29,904,256 bytes per
owned-buffer encode and 66,272 bytes per stream encode, excluding input setup.
The media working reservation was 65,548 bytes. Allocation, media reservations
and process peak RSS describe different costs. These shared-host measurements
establish the observed memory change; they do not establish a general speedup.

# WebP encoding

`Mediana.Codecs.Webp` writes lossless VP8L images and full-canvas WebP animations
in managed C#. It references only Mediana's codec and core contracts. No native
codec, external process or third-party image package is used at runtime.

WebP is a RIFF image container. Its image payload can use lossy VP8 or lossless
VP8L; extended files add metadata and animation chunks. Mediana currently encodes
VP8L, including alpha. Lossy VP8 encoding, WebP decoding and ICC/Exif/XMP output
remain open. See the official [RIFF container specification](https://developers.google.com/speed/webp/docs/riff_container)
and [lossless bitstream specification](https://developers.google.com/speed/webp/docs/webp_lossless_bitstream_specification).

## Still images

The input is a borrowed `VideoFrame` with straight RGBA32 pixels, full-range sRGB
color and square pixels. Padding and a nonzero plane offset are supported.
All RGBA bytes are preserved, including RGB values under zero alpha. Each
dimension must be at most 16,384 pixels, as required by VP8L's 14-bit fields.

```csharp
using MediaBuffer encoded = WebpEncoder.Encode(frame, allocator, cancellationToken);
await destination.WriteAsync(encoded.Memory, cancellationToken);
```

`Encode` returns an independent owned buffer containing the complete RIFF file.
Its capacity equals the encoded length. The caller owns both the source frame
and the returned buffer.

`WebpImageEncoder` implements the common `IImageEncoder` contract, alongside
`PngImageEncoder`. `EncodeAsync` accepts a writable non-seekable stream, retains
the input until completion and returns bytes written. It leaves the destination
open without flushing it. Concurrent calls have independent workspace. Failed
output must be discarded.

```csharp
IImageEncoder encoder = new WebpImageEncoder();
long bytesWritten = await encoder.EncodeAsync(frame, destination, allocator, cancellationToken);
```

`ImageEncoderFeatures.SampleAspectRatio` identifies encoders that can store
non-square pixels. PNG supports this feature; WebP requires display geometry
to be resolved into square pixels before encoding.

## Command line

The extension selects the encoder after the common input detection, frame
selection and sRGB conversion. The same path handles supported images, document
pages and video/animation frames. WebP output uses square-pixel display geometry;
color conversion and geometric sampling share one pass.

```sh
mediana -i photo.heic output.webp
mediana -i artwork.svg output.webp
mediana -i report.pdf page.webp --page 2
mediana -i input.mp4 frame.webp --time-ms 250
mediana -i sticker.tgs frame.webp --time-ms 100
```

These commands write one image. Animated WebP output is available through the
sequence API below; CLI animation export remains open.

## Animation

```csharp
long bytesWritten = await WebpAnimationEncoder.EncodeAsync(
    frames, destination, allocator, loopCount: 0, cancellationToken: cancellationToken);
```

`frames` is an `IAsyncEnumerable<VideoFrame>` in display order. The caller owns
yielded frames. Every frame must have the same canvas dimensions and a declared
duration that is an exact integer number of milliseconds in WebP's 24-bit range.
No timing rounding is applied. Loop count zero means infinite repetition.

Each ANMF frame contains a complete lossless image, replaces the canvas and
does not blend with the previous frame. Compression workspace is released
between frames. The destination must be writable and seekable: after enumeration,
the writer completes the RIFF size and animation alpha flag. It supports writing
at a nonzero stream position, leaves the stream open and positions it after the
completed file. Empty sequences, inconsistent canvases and unsupported timing
are rejected. Incomplete output on any failure must be discarded.

## Compression and memory

The encoder combines reversible subtract-green and block prediction transforms,
optional packed color indexing, bounded LZ77 match search and canonical Huffman
codes with length limits. SIMD handles subtract-green and predictor residuals;
the BCL vectorized prefix comparison measures LZ matches. Scalar paths implement
the same arithmetic where SIMD is unavailable. Uniform images bypass full-image
transform storage.

The supplied allocator accounts for pixel workspace, predictor images, token
blocks, hash/search tables and encoded output. Small coding tables, object
overhead and caller-owned stream storage are outside that budget. Source pixels
are borrowed; the encoder uses one full-size residual workspace for nonuniform
input and releases it before allocating the complete encoded output. Tokens are
stored in blocks proportional to actual token count, rather than a second
full-image array. Cancellation and failures release owned workspace.

The compression search currently favors bounded work. Color-cache coding,
cross-color transforms, spatial entropy groups and broader predictor/search
selection remain open; encoding speed and compressed size must be compared
together. See the [reproducible ImageSharp comparison](../benchmarks/Mediana.WebpBenchmarks/README.md).

## Validation

Managed tests cover exact RGBA, transparent RGB, odd dimensions, padded rows,
plane offsets, source guards, allocator cleanup, cancellation, original stream
errors, concurrent calls, pending input leases, length-limited prefix codes,
animation headers/timing and shared CLI routing. Shared image and animation
checks also run in the Native AOT test executable.

Independent decoding of 34 generated images, including both maximum-dimension
axes, and three photographs by both ImageSharp 4.1.3 and FFmpeg preserved every
RGBA byte. ImageSharp also decoded two three-frame animations with exact RGBA,
millisecond durations and repeat counts. The scalar encoding path was independently
checked with SIMD disabled. The full Native AOT test executable passed; a separate
native CLI produced exact matching PNG/WebP pixels for an SVG image, a video frame
and a PDF page. These checks validate the emitted bitstream; Mediana does not
expose a WebP decoder.

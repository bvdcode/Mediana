# WebP encoding

`Mediana.Codecs.Webp` writes VP8 and VP8L images and WebP animations in managed
C#. It references only Mediana's codec and core contracts. No native codec,
external process or third-party image package is used at runtime.

WebP is a RIFF image container. VP8 carries lossy image data; VP8L carries
lossless image data. Extended files add alpha, metadata and animation chunks.
The writer follows the [RIFF specification](https://developers.google.com/speed/webp/docs/riff_container),
[VP8L specification](https://developers.google.com/speed/webp/docs/webp_lossless_bitstream_specification)
and [VP8 decoding specification](https://www.rfc-editor.org/rfc/rfc6386).
WebP decoding is a separate capability and is not exposed by this module.

## Still images

Input is a borrowed `VideoFrame` with straight RGBA32 pixels, full-range sRGB
color and square pixels. Padding and a nonzero plane offset are supported.
Lossless compression preserves every RGBA byte, including RGB under zero alpha.
VP8L permits dimensions up to 16,384; VP8 permits up to 16,383 per axis.
Lossy color uses 4:2:0 sampling; its alpha channel remains exact.

```csharp
using MediaBuffer lossless = WebpEncoder.Encode(frame, allocator, cancellationToken);

WebpEncodingOptions options = new()
{
    Compression = WebpCompression.Lossy,
    Quality = 80
};
using MediaBuffer lossy = WebpEncoder.Encode(frame, allocator, options, cancellationToken);
```

Lossless is the default. Lossy quality ranges from 0 to 100; quality 100 still
uses lossy color coding. Quality does not alter lossless pixels. `Encode` returns
an independent owned buffer containing the complete RIFF file. Its capacity equals
the encoded length. The caller owns the source frame and returned buffer.

`WebpImageEncoder` implements `IImageEncoder`, alongside `PngImageEncoder`.
`EncodeAsync` accepts a writable non-seekable stream, retains input until
completion, leaves the destination open without flushing it and returns bytes
written. Concurrent calls have independent workspace. Failed output must be discarded.

```csharp
IImageEncoder encoder = new WebpImageEncoder(options);
long bytesWritten = await encoder.EncodeAsync(frame, destination, allocator, cancellationToken);
```

`ImageEncoderFeatures.SampleAspectRatio` identifies encoders that can store
non-square pixels. PNG supports it; WebP requires square-pixel display geometry.

## Metadata

`WebpEncodingOptions.Metadata` accepts a `WebpMetadata` with borrowed `IccProfile`,
`Exif` and `Xmp` memories. The writer carries their payloads unchanged, sets VP8X
flags and writes chunks in the required order with zero padding. The ICC profile
must describe the supplied pixels: embedding a profile does not perform a color
conversion. Keep all metadata memories alive and unchanged until encoding completes.

## Command line

The extension selects the encoder after common input detection, frame selection
and sRGB conversion. Images, document pages and video/animation frames share this
path. Color conversion and square-pixel geometric sampling use one pass.

```sh
mediana -i photo.heic output.webp
mediana -i artwork.svg output.webp --image-compression Lossy --quality 80
mediana -i report.pdf page.webp --page 2
mediana -i input.mp4 frame.webp --time-ms 250
mediana -i sticker.tgs animation.webp --animate
mediana -i input.mp4 animation.webp --animate --image-compression Lossy --quality 75
```

Without `--animate`, output is one image. `--animate` exports frames from a
registered animation sequence decoder or video session. `--time-ms` selects the
starting time; audio is omitted. Compression defaults to lossless. `--quality`
requires lossy compression and ranges from 0 to 100, defaulting to 75. PNG rejects
lossy quality options. TGS input is parsed once per sequence. Sequence export
reports processed media time, speed and output size through the common progress
reporter; remaining time is shown only when an estimate is available.

## Animation API

The full-canvas API accepts `IAsyncEnumerable<VideoFrame>` in display order.
Every frame has identical dimensions and a declared duration that is an exact
integer number of milliseconds in WebP's 24-bit range. Each frame replaces the
canvas. The caller owns yielded frames; the writer borrows them until their
asynchronous writes complete.

```csharp
long bytesWritten = await WebpAnimationEncoder.EncodeAsync(
    frames, destination, allocator, options, loopCount: 0, cancellationToken: cancellationToken);
```

For partial frames, use `IAsyncEnumerable<WebpAnimationFrame>` and
`WebpAnimationOptions(canvasWidth, canvasHeight)`. Each rectangle specifies even
`X`/`Y` coordinates, `Blending` (`Replace` or `AlphaBlend`), `Disposal` (`Keep` or
`Background`) and optional per-frame encoding options. VP8 and VP8L can coexist
in one animation. Global options specify loop count, background as 0xAARRGGBB,
default encoding and metadata. Metadata belongs to the animation, not individual
frame overrides. A zero loop count repeats indefinitely.

The destination must be writable and seekable so the writer can complete RIFF
size and alpha flags after enumeration. Writing at a nonzero stream position is
supported; the stream remains open and positioned after the completed file.
Compression workspace is released between frames. Empty sequences, out-of-canvas
rectangles, odd coordinates and unsupported timing are rejected. Incomplete output
on any failure must be discarded.

`WebpImageSequenceEncoder` adapts this writer to `IImageSequenceEncoder`.
Its `DurationTimeBase` is 1/1000. `VideoFrameTimeQuantizer` explicitly rounds start
and end boundaries to this clock and derives duration from their difference;
it retains the same pixel storage. Fractional 24/30/29.97 fps intervals therefore
do not accumulate independent rounding errors. The low-level WebP writer does
not silently round caller-supplied durations.

## Computation and memory

VP8L combines subtract-green and block prediction transforms, optional packed
color indexing, bounded LZ77 search and canonical length-limited Huffman codes.
SIMD handles subtract-green and prediction residuals; the BCL vectorized prefix
comparison measures LZ matches. Uniform images bypass full-image transform storage.
Lossy VP8 uses ten directional luma predictors, four chroma predictors, forward
DCT/quantization, normative integer reconstruction and arithmetic-coded coefficient
tokens. SIMD scores prediction errors. Raw and VP8L-compressed alpha are compared
by encoded size; the smaller representation is written without changing alpha.
Scalar paths implement the same arithmetic when hardware intrinsics are disabled.

The allocator accounts for pixel workspace, predictor images, token blocks,
hash/search tables and encoded output. Small coding tables, object overhead and
caller-owned stream storage are outside that budget. Source pixels are borrowed.
VP8L uses one full-size residual workspace for nonuniform input and releases it
before allocating final output. Literals occupy a 32-bit value plus one reference
flag bit; LZ references occupy two values. VP8 reconstruction retains one padded
macroblock row rather than a full decoded image. Animation holds compression
workspace for one frame at a time. Cancellation and failures release owned storage.

Compression speed, file size and lossy quality must be considered together.
See the [reproducible ImageSharp comparison](../benchmarks/Mediana.WebpBenchmarks/README.md)
for settings and individual measurements.

## Validation

Managed tests cover exact VP8L RGBA, transparent RGB, odd dimensions, padded rows,
plane offsets, guards, allocator cleanup, cancellation, write errors, concurrent
calls, pending input leases, arithmetic carries, mode contexts, predictor vectors,
SIMD error arithmetic, metadata ordering, mixed animation rectangles and CLI routing.
Clock tests cover 10,000 frames each at 24, 30 and 30000/1001 fps.

Independent FFmpeg decoding passed 84 VP8 files across seven geometries, four pixel
patterns and quality 0/75/100, preserving dimensions and exact alpha without decoder
errors. Earlier VP8L verification covered 34 generated images, including both
maximum-dimension axes, and three photographs with exact RGBA in ImageSharp and
FFmpeg. libwebp 1.6.0 decoded a 120-frame lossless TGS export totaling 2000 ms and
a 30-frame lossy video export totaling 1001 ms. ImageSharp 4.1.3 also decoded the
TGS sequence with the same frame count, duration and repeat count.

libwebp also verified the composited pixels of a mixed VP8/VP8L animation with
partial rectangles, alpha blending and background disposal, together with frame
delays and repeat count. All 199 WebP tests passed with hardware intrinsics
disabled. A Native AOT CLI produced still, TGS and video WebP outputs that
independent decoders opened successfully.

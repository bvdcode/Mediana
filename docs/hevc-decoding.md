# HEVC video decoding

`Mediana.Codecs.Hevc` provides managed progressive HEVC Main/Main10 decoding for
complete length-prefixed access units and an `hvcC` configuration. It uses the
same syntax, transforms, prediction and in-loop filters as HEIC reconstruction.
It requires no native codec or external multimedia library.

Register `HevcDecoderFactory` alongside the required container readers:

```csharp
MediaEngine engine = new MediaEngineBuilder()
    .AddContainer(new Mp4ReaderFactory(new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
    .AddContainer(new MatroskaReaderFactory())
    .AddVideoDecoder(new HevcDecoderFactory())
    .Build();

await using RandomAccessStreamByteSource source = new(File.OpenRead("input.mp4"));
await using VideoSession session = await engine.OpenVideoAsync(source);
await session.SeekAsync(new MediaTimestamp(10, new TimeBase(1, 1)));
using VideoFrame? frame = await session.ReadFrameAsync();
```

The namespaces are `Mediana`, `Mediana.Codecs.Hevc`, `Mediana.Containers.Mp4`,
`Mediana.Containers.Matroska`, `Mediana.Core.Timing`, `Mediana.Core.Video` and
`Mediana.IO`. To select the first frame at or after a target time, use
`ReadFrameAtOrAfterAsync(target)` after seeking. A seek begins at a validated IRAP;
required reference pictures are reconstructed and leading RASL pictures are
suppressed according to H.265 random-access rules.

The decoder supports I/P/B pictures, short/long-term reference selection,
spatial/temporal motion prediction, merge and asymmetric partitions, explicit
weights, fractional interpolation, transform residuals, deblocking, SAO, WPP,
uniform/explicit tiles and multiple independent slices. Output is in display order, with source packet PTS
and duration, conformance cropping, color labels and sample aspect ratio.
`HevcRandomAccessInspector` validates slice headers independently of container
key-frame flags.

Eight-bit output uses `VideoPixelFormat.Yuv420P8`. Ten-bit output uses
`VideoPixelFormat.Yuv420P10`: planar Y/Cb/Cr, 4:2:0, low ten bits of native-endian
unsigned 16-bit words. `GetRow` returns bytes; use `MemoryMarshal.Cast<byte, ushort>`
to read ten-bit samples. Plane stride can retain coded padding after cropping.
Output/reference pictures share pixel storage; frame publication does not copy a
picture. Each frame owns an independent lease which survives decoder reset or
disposal. Applications must dispose frames individually.

`SrgbConverter` and `SrgbPreview` accept both precisions for SDR conversion without
an intermediate eight-bit YUV buffer. Color conversion retains ten-bit precision
until RGBA output. HDR transfer functions require a separate tone-mapping operation.
The H.264 encoder currently accepts eight-bit YUV; a ten-bit source requires an
explicit precision conversion before encoding.

CLI preview and animation commands register both AVC and HEVC decoding:

```text
mediana -i input.mp4 preview.png
mediana video preview input.mkv preview.webp --time-ms 10000
```

One decoder instance is sequential; calls must not overlap. Drain ready output
before retrying a packet that returned `OutputRequired`. `CompleteInput` flushes
reordered frames. Failure or cancellation requires reset or disposal. Reset restores
the original parameter sets and requires a new IRAP. Decoder options expose optional
pixel, configuration-byte and NAL-count limits; defaults impose no application-size
cap beyond structural representation. The supplied allocator budgets pixel and
motion storage. Pooled syntax/filter metadata and object overhead are additional.

Tiles, dependent slice segments, PCM, multilayer/interlaced video and range-extension
tools remain unsupported and are rejected explicitly. Ten-bit decoding is separate
from HDR display conversion.

Verification compares every visible Y/Cb/Cr sample of 208 display-order pictures
against independent FFmpeg decoding. Nine synthetic sequences cover both precisions,
I/P/B prediction, open/closed GOP, crop/SAR, explicit weights, WPP, independent slices
and several transform/reference configurations. Repeated forward/reverse seeks,
end-of-input, cancellation/reset, backpressure, timestamps and retained frame ownership are
also checked. Independent HM-18.0 references cover 129,888 arithmetic bins, 1,280
fractional predictions, 2,592 weighted predictions and 768 complete motion blocks.

Syntax and reconstruction follow [ITU-T H.265](https://www.itu.int/rec/T-REC-H.265).
Independent reference software is [HEVC HM](https://hevc.hhi.fraunhofer.de/).

A 30-second 640x360, 24 fps decode check exercises 720 complete frames per pass.
Reusing the current-picture motion field reduces total managed allocations from
about 460 MiB to 262 MiB per pass (43%). The allocator's observed live peak grows
from about 2.13 MiB to 2.40 MiB because that field remains available for reuse.
Retained temporal references keep independent compact fields. Alternating old/new
process runs show no material CPU change from this storage change; whole HEVC
decoding remains slower than FFmpeg's single-thread decoder. These figures describe
this sequence and include owned output buffers; they are not a general throughput
claim or a zero-allocation claim for public frame publication.

Tile syntax follows H.265 sections 6.5.1, 7.4.7, 8.6 and 9.3. Coding order uses
tile-scan addresses; container timing and output remain in picture display order.
Each tile starts with initialized CABAC contexts and QP prediction. WPP rows
within a tile synchronize from that tile's second CTU. Prediction and SAO merges
cannot cross tiles; deblocking and SAO filtering respect both tile and slice
filter flags. Entry-point offsets include encoded emulation-prevention bytes.
The same tile geometry and entropy handling serve HEIF still-image decoding.

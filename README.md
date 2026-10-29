# Mediana

Mediana is an MIT-licensed multimedia library under development for .NET 10.
Its media implementation is written in C#, with no FFmpeg, external media processes,
native media libraries, or third-party runtime packages.

The current implementation is experimental. It reads metadata and compressed packets from
non-fragmented MP4/MOV files containing AVC/AAC tracks. The H.264 source module
connects limited CAVLC/CABAC I/P decoding to public video sessions and returns cropped YUV frames.
The sample can extract a supported video frame, convert SDR color to sRGB, correct
the sample aspect ratio and right-angle orientation, and save a PNG preview.
An experimental MPEG-TS source module packages AVC/AAC access units without decoding.
**B-picture decoding, audio decoding and video/audio encoding are not available yet.**

## Components

| Project | Current responsibility |
| --- | --- |
| `Mediana.Core` | Rational time, stream metadata, packets, video/PCM frames, buffer ownership and allocation budgets |
| `Mediana.IO` | Sequential and random access byte sources, stream ownership and probe replay |
| `Mediana.Containers` | Reader/factory contracts and a writer finalization contract |
| `Mediana.Containers.Mp4` | Non-fragmented MP4/MOV metadata and AVC/AAC packet reading |
| `Mediana.Containers.MpegTs` | Experimental single-program AVC/AAC transport-stream writer; source project only |
| `Mediana.Codecs` | Separate input/output codec protocol and video decoder factories |
| `Mediana.Codecs.H264` | Experimental AVC CAVLC/CABAC I/P decoder factory, picture ordering and cropped YUV output; source project only |
| `Mediana.Codecs.Png` | Experimental sRGB RGBA32 PNG encoder; source project only |
| `Mediana.Processing` | Scalar SDR conversion to sRGB, RGBA32 resizing and square-pixel preview fitting |
| `Mediana.Streaming.Hls` | Experimental AVC MP4 video boundary planning with inspected IDR headers; source project only |
| `Mediana.Execution` | Pull-driven video decoding, delayed output, bounded container reads and cancellation |
| `Mediana` | Explicit registration, container detection and forward-only video sessions |
| `Mediana.Sample` | Real MP4/MOV packet inspection, PNG previews and generated-frame checks |

Core does not reference the other components. Containers do not reference codecs,
and codecs do not perform I/O. The public facade composes these contracts.

The H.264 work currently reconstructs single-slice progressive 8-bit 4:2:0 I/P
pictures with CAVLC or CABAC and default scaling, including motion compensation, reference
picture management, intra prediction, inverse transforms, PCM blocks and deblocking.
Two complete Baseline/Main CAVLC sequences and one cropped IDR picture match the
independent filtered YUV references sample for sample (nineteen pictures total).
The Main sequence includes explicit prediction weights and reference-list changes.
Two High CAVLC sequences with 4x4/8x8/16x16 intra prediction, inter 8x8 transforms,
motion, weighted prediction and nonzero deblocking offsets match thirty further
independent YUV pictures internally and through public sessions.
Three High CABAC sequences with the same transform sizes match forty-two further
independent YUV pictures, including all three CABAC initialization selections.
All forty-nine CAVLC pictures, six Main CABAC intra pictures and 96 additional CABAC I/P pictures match through MediaEngine,
with timing, cropping and sample aspect ratio preserved. Preview generation requires
the supported CAVLC/CABAC I/P subset and known color and sample aspect ratio;
it does not cover general H.264 files.

## Build and run

Install a .NET 10 SDK. The repository accepts newer .NET 10 feature bands.

```sh
dotnet tool restore
PACKAGE_VERSION=$(dotnet gitversion /showvariable SemVer)
dotnet restore Mediana.slnx
dotnet build Mediana.slnx -c Release --no-restore -p:Version="$PACKAGE_VERSION"
dotnet test Mediana.slnx -c Release --no-build --no-restore
dotnet run --project tests/Mediana.NativeTests -c Release --no-build --no-restore
dotnet format Mediana.slnx --verify-no-changes --no-restore
dotnet run --project samples/Mediana.Sample -c Release --no-build -- --self-test
dotnet run --project samples/Mediana.Sample -c Release --no-build -- --probe-mp4 tests/Mediana.Tests/Data/Mp4/avc-aac.mp4
dotnet run --project samples/Mediana.Sample -c Release --no-build -- --preview tests/Mediana.Tests/Data/Mp4/avc-weighted.mp4 artifacts/preview.png 0 250 100 100
dotnet run --project samples/Mediana.Sample -c Release --no-build -- --preview tests/Mediana.Tests/Data/Mp4/avc-weighted-rotated.mp4 artifacts/preview-rotated.png 0 0 100 100
dotnet run --project samples/Mediana.Sample -c Release --no-build -- --preview tests/Mediana.Tests/Data/Mp4/avc-cabac-intra.mp4 artifacts/preview-cabac.png 0 450 100 100
dotnet run --project samples/Mediana.Sample -c Release --no-build -- --preview tests/Mediana.Tests/Data/Mp4/avc-cabac-p2.mp4 artifacts/preview-cabac-p.png 0 450 100 100
dotnet run --project samples/Mediana.Sample -c Release --no-build -- --preview tests/Mediana.Tests/Data/Mp4/avc-cavlc-8x8-weighted.mp4 artifacts/preview-high-cavlc.png 0 450 100 100
dotnet run --project samples/Mediana.Sample -c Release --no-build -- --preview tests/Mediana.Tests/Data/Mp4/avc-cabac-8x8-2.mp4 artifacts/preview-high-cabac.png 0 450 100 100
```

The self-test decodes a generated 2×2 RGBA packet through the codec protocol, resizes
it to 1×1, checks the red output pixel and exact time conversion, then verifies
that all leased buffer bytes have been released. It does not open a video file.

The preview command takes input, a new output path, video stream index, target
milliseconds in the media timeline, maximum width and maximum height. In this example,
it selects the first frame at or after 250 ms (300 ms) and writes a 100×49 sRGB PNG
with square pixels. The output directory must exist. Existing files are not replaced;
a request beyond the last frame fails without creating an image.
The rotated fixture writes 49×100 after applying its display matrix and SAR.
The CABAC intra fixture selects 600 ms for a request at 450 ms and writes 100×49.
The CABAC P fixture selects 500 ms for the same request and writes 100×49.
The High CAVLC and CABAC 8x8 fixtures select 500 ms and write 100×50.

Publish and execute it with Native AOT on Linux x64:

```sh
dotnet publish samples/Mediana.Sample -c Release -r linux-x64 -m:1 -p:IlcSingleThreaded=true -p:Version="$PACKAGE_VERSION" -o artifacts/native/linux-x64
./artifacts/native/linux-x64/Mediana.Sample --self-test
./artifacts/native/linux-x64/Mediana.Sample --probe-mp4 tests/Mediana.Tests/Data/Mp4/avc-aac.mp4
./artifacts/native/linux-x64/Mediana.Sample --preview tests/Mediana.Tests/Data/Mp4/avc-weighted.mp4 artifacts/preview-native.png 0 250 100 100
dotnet publish tests/Mediana.NativeTests -c Release -r linux-x64 -m:1 -p:IlcSingleThreaded=true -p:Version="$PACKAGE_VERSION" -o artifacts/native-tests/linux-x64
./artifacts/native-tests/linux-x64/Mediana.NativeTests
```

Native AOT requires the platform build toolchain. The resulting executable targets
one OS/architecture and still uses standard OS runtime facilities.
See the [.NET Native AOT prerequisites](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/).

Mediana.NativeTests executes internal H.264 I/P reconstruction and public MediaEngine
video sessions. Each path compares all visible YUV samples in forty-nine CAVLC pictures
with the checked-in references. Public sessions also compare six CABAC intra pictures and 96 CABAC I/P pictures
and verify timing, cropping,
sample aspect ratio and allocation release. Its fixture loading and comparison code
is shared with the unit tests. The native checks also compare SDR conversion with
an independent sRGB reference and decode a square-pixel PNG preview to verify every
sample, CRC, color tag and pixel aspect ratio, including the rotated MP4 fixture.
Internal CABAC decision, bypass and termination arithmetic is checked against a
separate arbitrary-precision interval writer that shares the normative probability
constants. These arithmetic components and checks are adapted from the MIT-licensed
[Heic.NET](https://github.com/bvdcode/Heic.NET), with H.264 context initialization
and bit reading. Internal checks also compare 227 I-slice contexts at every 8-bit
quantizer with a separate initialization reference and read the beginning of a
real CABAC IDR. P-slice initialization is checked for 252 contexts, all 52 quantizers
and all three initialization selections; the first real non-skipped P partition
matches an independent macroblock trace. The shared residual reader is checked
against exact intervals with I and all P initialization selections.
Internal P-slice syntax checks cover skip flags, all 30 permitted macroblock types,
the four sub-macroblock types, reference indices and signed motion differences,
including unary/Exp-Golomb boundaries and invalid precision/reference limits.
Real I/P slice headers match an independent header trace.
Internal CABAC reconstruction also matches seven independent filtered YUV pictures,
including mixed 4x4/16x16 intra prediction, cropping and nonzero deblocking offsets.
The public decoder reconstructs supported CABAC I/P pictures, including weighted
prediction, multiple references and per-partition motion differences. Four complete
CABAC I/P sequences match 54 independent YUV pictures with all three cabac_init_idc
selections, mixed intra/inter prediction and cropped storage.
Eight-bit CAVLC and CABAC support 8x8 intra prediction/transforms; all nine
directional prediction modes match explicit independent vectors. Three High CABAC
sequences match 42 additional complete I/P pictures with actual intra/inter 8x8
use. The 34 progressive 8x8 contexts match all 7072 normative initial states;
residual intervals exercise every scan position, signs and large magnitudes.
B pictures and broader slice support remain open. Unsupported or damaged pictures fault the session
and release pending output; reset or disposal is required. Unused bits
in the arithmetic terminal byte are discarded; following zero words are checked.
This decoder is not a complete bitstream conformance validator.
CI is configured to build and run both native
executables on Linux, Windows and macOS.

## Reading MP4/MOV

Install the optional Mediana.Containers.Mp4 package to read supported files directly:

```csharp
MediaBufferAllocator allocator = new(64 * 1024 * 1024);
await using RandomAccessStreamByteSource source = new(File.OpenRead(path));
await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
IReadOnlyList<Mp4TrackInfo> tracks = reader.Tracks;
using MediaPacket? packet = await reader.ReadPacketAsync();
```

The namespaces are Mediana.Containers.Mp4, Mediana.Core, Mediana.Core.Memory and
Mediana.IO. Mp4ReaderFactory connects the same reader to MediaEngineBuilder.
H264DecoderFactory connects the source H.264 module to decoded video sessions.

The reader requires random access and a known source length. It scans box headers,
loads the bounded movie metadata and reads individual samples on demand. It does
not load the media data box while opening. It supports movie metadata at either
end of the file, sample/chunk mapping, decode time runs, signed/unsigned composition
offsets, sync samples and 32/64-bit chunk offsets. Packets are interleaved by decode
time; each track retains its sample order.

`GetSampleInfo(streamIndex, sampleIndex)` resolves the absolute source offset,
encoded size, PTS/DTS, duration and container sync indication without reading media
bytes or allocating a per-sample index. `ReadPacketAtAsync(streamIndex, sampleIndex)`
reads just that encoded sample and returns an owned packet. Both indices are
zero-based; sample order is decode order. These operations preserve every sequential
cursor, also after end of stream, and use the selected timestamp mode. Lookup scans
the compact chunk/timing runs and the sample sizes inside the containing chunk;
it does not read preceding payloads. The mapping follows the
[QuickTime sample lookup](https://developer.apple.com/documentation/quicktime-file-format/using_sample_atoms).
Successive sample lookups advance a separate metadata cursor so a sequential
metadata scan stays linear even when many samples share one chunk.
Reader operations must be sequential. A packet read by index does not initialize
or reset a decoder, and a sync declaration alone does not verify codec-level
segment independence.

AVC streams expose the original avcC configuration and length-prefixed NAL packets.
AAC streams expose the AudioSpecificConfig and raw access units. Container-declared
audio channels and sample rate are labelled explicitly: codec configuration may
describe different values, which require codec-level interpretation.

Movie duration, track presentation duration, original media duration and edit lists
remain separate. By default, packets use original media timestamps. Select
`Mp4ReadOptions.TimestampMode = Mp4TimestampMode.LinearMovie` to translate one
unit-rate media edit, optionally preceded by one empty edit, into movie time.
Packet PTS/DTS and durations use an exact rational time base, and tracks are merged
by their translated DTS. `reader.Streams` describes that packet time base;
`reader.Tracks` preserves original declarations and edits. Unsupported edit shapes
or an unrepresentable exact time base fail explicitly in this mode. The translation
follows the [QuickTime edit-list mapping](https://developer.apple.com/documentation/quicktime-file-format/edit_list_atom).

Both modes retain every compressed sample, including negative decoder priming,
audio padding and samples outside the edit interval. LinearMovie translates time;
it does not trim media, implement repeated segments or apply non-unit playback
rates. The reader does not apply rotation/display transforms or seek. A sync-sample
flag alone does not establish HLS segment independence.

Mp4Reader.DisplayMatrix and Mp4TrackInfo.DisplayMatrix preserve the movie and track
header matrices. A video stream's EncodedStreamInfo.DisplayMatrix composes the track
transform followed by the movie transform. VideoSession.Stream exposes this selected
stream metadata; decoding leaves the raster unchanged. Matrix interpretation follows
the [QuickTime matrix definition](https://developer.apple.com/documentation/quicktime-file-format/matrices).

Fragmented files, encrypted/external data, compressed QuickTime headers, multiple
sample descriptions and other sample/handler types fail explicitly. The current
sample entries are avc1/avc3 and version-zero mp4a with AAC object type 2, 5 or 29.
Empty samples, sample sizes above 2 GiB and time scales above Int32.MaxValue are
unsupported. Metadata, codec configuration, boxes, tracks, samples and edits have
configurable limits through Mp4ReadOptions.

Movie storage, header scratch space and returned packet arrays share the supplied
allocator budget. Immutable stream configuration snapshots and managed object
overhead are outside that byte-array accounting; their size/count has separate
limits. Disposing the reader releases its movie storage; retained packets remain valid.

## Decoding H.264

Reference the Mediana.Codecs.H264 source project to use its experimental factory;
this module is not included in the NuGet packages yet. Register both modules explicitly:

```csharp
MediaEngine engine = new MediaEngineBuilder()
    .AddContainer(new Mp4ReaderFactory())
    .AddVideoDecoder(new H264DecoderFactory())
    .Build();
await using RandomAccessStreamByteSource source = new(File.OpenRead(path));
await using VideoSession session = await engine.OpenVideoAsync(source, streamIndex: 0);
using VideoFrame? frame = await session.ReadFrameAtOrAfterAsync(
    new MediaTimestamp(1, new TimeBase(1, 1)));
```

The namespaces are Mediana, Mediana.Codecs.H264, Mediana.Containers.Mp4,
Mediana.Core.Timing, Mediana.Core.Video and Mediana.IO. The example selects the first
remaining frame at or after one second in the original media timeline; it decodes
forward without seeking. Null means the stream has ended.

`H264RandomAccessInspector` examines AVC packets without reconstructing pictures.
`IsIdr(packet)` ignores container key-frame flags and validates access-unit boundaries
and the supported IDR slice header, including its parameter references. It requires
a complete single-slice progressive 8-bit 4:2:0 IDR picture; it does not validate
residual data. Non-IDR slice headers, including B pictures, are not parsed by this
inspection operation. Packet inspection uses the original configuration and any
parameter sets carried within that packet, without retaining state between calls.

The decoder accepts AVC length-prefixed packets with one complete single-slice
picture and an initial avcC configuration. Supported pictures are progressive
8-bit YUV 4:2:0 CAVLC/CABAC I/P with default scaling, including 4x4/8x8
prediction and transforms.
Decoding begins with an IDR; sequence changes through in-band SPS/PPS also
require an IDR. Each output carries
its own format, color information, sample aspect ratio, presentation timestamp and
duration. Cropping is applied to YUV storage; rotation, edit lists and display aspect
transforms are not applied. Missing packet timestamps remain absent; unspecified
sample aspect ratio and CICP color codes remain unspecified.

Picture order types 0, 1 and 2, bounded delayed output, IDR/MMCO resets, input
completion, cancellation and buffer ownership have executable checks. SEI message
boundaries are validated; payload semantics are not interpreted. B pictures,
multiple slices, interlacing, custom scaling, higher bit depths and inferred
references for frame-number gaps are unsupported. H264DecoderOptions bounds picture
pixels, configuration bytes, parameter-set bytes, NAL count and reference operations.
Returned frames remain valid after reset or disposal until their owners release them.

SrgbConverter.Apply from Mediana.Processing converts supported YUV420P8 or straight
RGBA32 to straight sRGB RGBA32. It supports full/limited YCbCr range, five conventional
YCbCr matrices, all six progressive chroma locations and common D65 primaries.
Conversion uses linear-light primary transforms and an ideal BT.1886 reference
display for BT.709/SMPTE 240M signals; RGB excursions and the target sRGB gamut are
clipped. Alpha, time and sample aspect ratio are preserved. HDR tone mapping and
unknown color interpretation fail explicitly. An overload accepts an explicit source
color interpretation; values are not guessed from the frame size.
The first weighted reference picture matches independent sRGB RGBA within one 8-bit
code per channel. The numerical definitions follow [ITU H.264 Annex E](https://www.itu.int/rec/T-REC-H.264-202408-S/en),
[BT.1886](https://www.itu.int/rec/R-REC-BT.1886/en) and
[sRGB in CSS Color 4](https://www.w3.org/TR/css-color-4/#predefined-sRGB).

## Creating a PNG preview

With a supported decoded frame and an allocation budget:

```csharp
VideoDisplayMatrix displayMatrix = session.Stream.DisplayMatrix
    ?? throw new NotSupportedException("A display interpretation is required.");
using VideoFrame preview = SrgbPreview.Fit(frame, displayMatrix, maximumWidth: 320, maximumHeight: 180, allocator);
using MediaBuffer png = PngEncoder.Encode(preview, allocator);
await output.WriteAsync(png.Memory, cancellationToken);
```

The additional namespaces are Mediana.Processing, Mediana.Codecs.Png and
Mediana.Core.Memory. Reference the Mediana.Codecs.Png source project; it is not
packaged yet. The encoder returns owned PNG bytes and does not open files or streams.

SrgbPreview.Fit combines SDR conversion, aspect correction, orthogonal orientation
and nearest-neighbor fitting. Its output matches SrgbConverter.Apply followed by
DisplayResize.Fit; it converts only selected source pixels and allocates one
preview frame instead of an intermediate full-resolution RGB frame. It requires
known color and SAR, preserves time and straight alpha, and produces square-pixel
sRGB RGBA32. The sample uses this combined path. Separate color conversion and
geometry operations remain available.

DisplayResize.Fit uses storage width times SAR and the storage height as the
unscaled display size, fits within the requested box without enlargement, rounds to
the nearest pixel and produces square pixels. It uses nearest-neighbor sampling,
preserves color and time, and requires known SAR or an explicit caller interpretation.
NearestNeighborResize.Apply instead resizes storage and preserves the original SAR.
The display-matrix overload supports the eight exact right-angle rotations and
reflections. It applies aspect correction before orientation and fits using the
final axes in a single sampling pass. Canvas translation is normalized to the
image's tight bounding rectangle; canvas padding is not added. Scaling, shear,
perspective and approximate rotations are rejected. The overload without a matrix
fits the stored raster without orientation. Neither operation maps edit lists.

PngEncoder accepts straight RGBA32 sRGB with known SAR. It writes non-interlaced
8-bit RGBA, an sRGB rendering intent and pHYs pixel dimensions, using BCL zlib
compression and unfiltered scanlines. Alpha is preserved. PNG framing and CRCs follow
the [PNG specification](https://www.w3.org/TR/png-3/). Encoding reserves a bounded
output array; the returned buffer exposes its actual encoded length while the
allocator counts the complete backing capacity. BCL compression workspace is outside
that budget. Adaptive filters, other pixel layouts, other color encodings and PNG decoding
are not implemented.

The generated previews from the first and fourth weighted reference pictures were
read by an independent PNG decoder and compared with independent SDR-to-sRGB output.
All samples differed by at most one 8-bit code. The fourth picture was selected at
300 ms for a request at 250 ms. Both previews preserve the source display ratio
within integer-dimension rounding. The rotated MP4 also produces a 49×100 image
whose orientation, color and aspect match independent inspection and decoded
reference samples. Movie-to-media time mapping and broader display transforms
remain open; timestamps used here refer to the original media timeline.
The CABAC intra fixture also produces a 100×49 PNG from the picture at 600 ms
for a request at 450 ms, with all samples within one code of an independent
sRGB reference. All three CABAC P initialization fixtures select 500 ms for the
same request and produce 100×49 PNGs matching independent sRGB references within
one code per channel. The High CAVLC and CABAC 8x8 fixtures also select 500 ms and
produce square-pixel 100×50 PNGs within one code of their independent sRGB references.
Unit tests and Native AOT execute the same preview checks.

## Writing an MPEG-TS program

Reference the Mediana.Containers.MpegTs source project and configure the output
with one to sixteen AVC/AAC streams. The module depends on container/core
contracts and does not require a video or audio decoder.

```csharp
await using MpegTsWriter writer = MpegTsWriter.Create(output, reader.Streams, allocator);
while (await reader.ReadPacketAsync(cancellationToken) is MediaPacket packet)
{
    using (packet)
    {
        await writer.WritePacketAsync(packet, cancellationToken);
    }
}
await writer.CompleteAsync(cancellationToken);
```

The namespaces are Mediana.Containers.MpegTs and Mediana.Core. Input packets
contain one complete access unit with explicit PTS and DTS. Decode timestamps
must increase within each track and must not decrease across calls. The writer
rescales to the 90 kHz clock with nearest-even rounding and adds the same
configurable offset to all timestamps; the default is one second. Timestamps
must leave a 100 ms lead for the program clock. Gaps above the configured limit
(default sixty seconds) require a new writer.

AVC requires a length-prefixed configuration and an initial IDR access unit on
each video track. The writer converts NAL lengths to Annex B delimiters, writes
access-unit delimiters and repeats configured SPS/PPS before IDR pictures.
Changing parameter sets, data partitioning and AVC extensions are unsupported.
B pictures can be copied as compressed data. AAC requires AAC-LC, indexed sample
rates, channel configurations 1 through 7 and 1024-sample frames; each raw access
unit is wrapped in one ADTS frame. HE-AAC, program-config elements and other
audio codecs are unsupported.

The output carries PAT/PMT, separate PES timestamps, continuity counters,
adaptation stuffing and PCRs at intervals no greater than 40 ms. Writes work on
a non-seekable Stream, borrow its lifetime and use the supplied allocator for
transport/PES scratch storage and framed access units. CompleteAsync flushes
without closing the destination. Any failed operation faults the writer and
releases its buffers; unfinished or failed output must be discarded.

The writer uses the caller's timeline. The MP4 reader defaults to original media
timestamps; select LinearMovie for the supported edit-list translation before
stream copy. Audio priming/discard metadata and container display matrices are not
represented in TS. This operation does not yet define HLS segment boundaries,
seek or playlists.

The sample copies every AVC/AAC access unit from a supported MP4/MOV into a new
TS file, applying LinearMovie timestamps and the writer's one-second offset:

```sh
dotnet run --project samples/Mediana.Sample -c Release -- \
  --remux-mp4 input.mp4 output.ts
```

The sample requires an initial IDR on each video track and timestamps that leave
the required program-clock lead after the offset. It preserves priming, padding
and samples outside the edit interval, so it does not perform sample-accurate
trimming. The output path must be new. A failed conversion removes its temporary
file and leaves an existing destination unchanged. The same command works with
the published Native AOT sample executable.

Tests remux three real MP4/MOV inputs, including AVC with reordered timestamps,
AAC and a High CABAC video-only stream. They compare every compressed slice and
AAC access unit, shifted PTS/DTS in both timeline modes, program tables, PCR cadence,
counters and buffer release in managed execution and Native AOT. Exact byte vectors
also cover framing, CRC, timestamp markers and packet-boundary stuffing.
The transport syntax follows [ITU-T H.222.0](https://www.itu.int/rec/T-REC-H.222.0-201410-S/en).

## Planning AVC video windows for HLS

Reference the Mediana.Streaming.Hls source project and open the MP4 reader in
LinearMovie mode. `Mp4AvcSegmentPlan.CreateAsync(reader, videoStreamIndex, options)`
uses metadata for the complete video timeline and reads payloads only for the
first sample and container sync candidates. Candidate IDR headers are inspected;
ordinary I pictures or false sync flags do not create boundaries. The first
sample must be an IDR. Each subsequent boundary is the first eligible IDR at or
after TargetDuration from the current segment start, rounded up to a video tick.

Each immutable entry records a zero-based first sample/count, presentation start,
video duration and an exclusive decode window. Decode order remains distinct from
presentation order. The final duration follows the last video presentation end.
Nonpositive sample durations, decreasing DTS, leading pictures or presentation
overlap at a chosen IDR boundary fail explicitly. MaximumSegments bounds the plan;
the reader allocator bounds candidate packet storage. Planning and random boundary
reads preserve sequential reader positions.

```sh
dotnet run --project samples/Mediana.Sample -c Release -- \
  --plan-hls-mp4 input.mp4 0 2250
```

The 7.2-second closed-GOP AVC/AAC fixture produces video windows starting at
0, 3 and 6 seconds with durations 3, 3 and 1.2 seconds. Managed and Native AOT
checks verify exact rational times, sample windows and reverse boundary reads.
This is a video boundary plan: audio coverage, continuous transport state,
segment output and playlists are still open. It does not claim full HLS support
or emit INDEPENDENT-SEGMENTS. Segment requirements follow
[RFC 8216](https://www.rfc-editor.org/rfc/rfc8216.html).

## Connecting modules

Applications supply container and decoder factories; none are registered implicitly.
Factories declare stable implementation identifiers, and decoder factories also
declare the codec identifier and validate a specific stream configuration.
Factories must have immutable descriptors and support independent session creation.

Duplicate implementation identifiers are rejected when registering. If two factories
match the same input/configuration, opening fails explicitly. There is no arbitrary
selection based on registration order.

With application-provided `containerFactory` and `decoderFactory`:

```csharp
MediaEngine engine = new MediaEngineBuilder()
    .AddContainer(containerFactory)
    .AddVideoDecoder(decoderFactory)
    .Build();

await using StreamByteSource source = new(File.OpenRead(path));
await using VideoSession session = await engine.OpenVideoAsync(
    source,
    streamIndex: 0,
    options: new MediaOpenOptions { BufferBudgetBytes = 64 * 1024 * 1024 },
    cancellationToken: cancellationToken);

using VideoFrame? frame = await session.ReadFrameAtOrAfterAsync(
    new MediaTimestamp(5, new TimeBase(1, 1)),
    cancellationToken);
```

The example requires namespaces `Mediana`, `Mediana.IO`, `Mediana.Core.Timing`
and `Mediana.Core.Video`. The caller chooses the actual video stream index.
The timestamp is in the stream's presentation timeline, not a normalized movie timeline.

`ReadFrameAtOrAfterAsync` scans **remaining** decoded frames and returns the first
presentation timestamp at or after the target. It does not seek, return the nearest
frame, infer missing timestamps or replace an out-of-range request with an earlier
frame. End of stream returns null. A frame without a timestamp causes a clear error.

Opening borrows the caller's source for the session lifetime. Sequential sources
are consumed even if opening fails. Random access sources open at their origin
and preserve the caller's sequential cursor. A session owns its reader, decoder
and input cursor or probe replay state.
The source is disposed separately by its caller. Session operations and disposal
must not overlap. Frames already returned remain valid after the session is disposed.

## Random access sources

RandomAccessStreamByteSource requires a readable, seekable Stream. Its ReadAtAsync
and ReadExactlyAtAsync operations use offsets relative to the stream's initial
position and preserve its sequential cursor, including after a failed read.
Reads at or beyond the captured length return EOF; an exact read throws if the
requested range is truncated. The underlying stream must remain exclusively owned
by the source while it is in use. Reads and disposal must not overlap.

VideoSession remains forward-only. Random access to bytes alone does not implement
container indexing, decoder reset or video seeking.

RandomAccessCursorByteSource provides an independent sequential cursor over a
random access source, while preserving range reads and the source's own cursor.

## Buffer ownership

`MediaBufferAllocator` limits the total byte-array capacity currently held by its
leases. Allocation fails immediately if the requested storage exceeds that budget.
Buffers are zero-initialized. There is currently no retained buffer pool.

```csharp
MediaBufferAllocator allocator = new(1024);

using WritableMediaBuffer writable = allocator.Allocate(16);
writable.Memory.Span.Fill(255);

using MediaBuffer pixels = writable.Publish();
VideoFormat format = new(2, 2, VideoPixelFormat.Rgba32);
using VideoFrame frame = new(format, pixels, format.CreateTightLayout());
using VideoFrame retained = frame.Retain();
```

The relevant namespaces are `Mediana.Core.Memory` and `Mediana.Core.Video`.
Publishing transfers ownership and invalidates the writable owner. Frames, packets
and audio frames retain their own read-only lease. `Retain` shares storage without
copying; every lease must be disposed independently. Slice returns an independent
lease over a logical range; the allocator still counts the complete backing array.

Writable owners are single-consumer objects. Borrowed `Memory` or `Span` views must
not be used after publication/disposal or concurrently with disposal. A retained
read-only lease permits independent ownership, not mutation. The API cannot revoke
a view previously obtained by a caller.

The budget covers allocations made through that allocator. It does not cap process
RSS, object metadata, stream buffers, third-party module allocations, or arrays
awaiting garbage collection. Custom modules must use the supplied allocator for
their media storage to participate in its accounting.

## Time, frames and execution

- `TimeBase` stores positive rational seconds per tick. Timestamps use signed
  64-bit values and checked 128-bit intermediate arithmetic. Rescaling requires an
  explicit rounding rule. Unknown timestamps are nullable; default time bases are invalid.
- Packets distinguish presentation and decode timestamps. Decoders must produce
  frames in presentation order. Constant frame rate is not assumed.
- Video supports custom byte-addressable plane descriptions, positive strides,
  and explicit color metadata. RGBA32 and planar YUV420P8 layouts are predefined.
  Resize currently accepts the predefined RGBA32 layout only and preserves color
  metadata without performing color conversion.
- Audio storage supports explicit channel order, native-endian PCM samples,
  planar/interleaved layouts and exact sample-count duration. Audio decoding,
  resampling, delay/padding correction and playback are not implemented.
- A codec can accept several packets before output, or produce several frames
  from one packet. Completing input drains delayed output; resetting discards it.
  Input is borrowed for a call, retained input needs a separate lease, and output
  belongs to its caller.
- Execution is sequential and demand-driven. It does not prefetch unbounded queues
  or launch worker pools. One codec instance is not called concurrently.
- Cancellation is cooperative. Cancellation or decode failure makes the session
  terminal and releases decoder state; dispose the session to release reader state.
- `MaxPacketReads` counts all container reads, including skipped streams and EOF
  checks. `MaxFrameReads` counts session reads, including EOF checks and frames
  consumed while selecting a timestamp. These are work limits, not timeouts.

No source or module is selected through reflection or runtime assembly discovery.
Libraries enable AOT/trimming analysis, and compiler/analyzer warnings fail the build.

## Validation and current boundaries

The automated tests exercise signed time conversion, overflow, shared-buffer
ownership, concurrent releases, allocation limits, video strides and subsampling,
PCM layouts, non-seekable short reads, probe replay, registration conflicts,
unsupported configurations, delayed and multiple codec outputs, EOF, cancellation
during I/O, invalid codec states and cleanup. Real MP4/MOV fixtures compare every
packet's data hash, size, timestamps, duration and sync flag with independent
reference results. Corrupted tables, truncated input and resource limits are checked.

The Native AOT sample has been built and executed locally on Linux x64.
The workflow defines build, formatting, tests and native publication/execution on
Linux, Windows and macOS. Other platform results depend on those CI runs.
The native sample covers the generated-frame pipeline, reading all packets from
the two original MP4/MOV fixtures and an MP4-to-PNG preview. The separate native tests
also verify internal and public decoding of forty-nine CAVLC reference pictures and
six CABAC intra pictures and 96 additional CABAC I/P pictures, SDR conversion and decoded PNG pixels. Each future
format also requires an executable AOT integration test.

Current limitations include broader compressed codec support, other container formats, seeking,
movie-to-stream timeline mapping, arbitrary changes within a coded sequence, broader display transforms,
broader color conversion, audio processing, video/audio encoding, other muxers, scheduling,
pooling, SIMD optimization and playback. The contracts are experimental and may
change before 1.0.

Production format scope and performance targets remain open. No throughput or
quality comparison against another media implementation has been established.

## Packages

```sh
dotnet pack Mediana.slnx -c Release --no-build --no-restore -p:Version="$PACKAGE_VERSION" -o artifacts/packages
```

The library projects depend at runtime only on .NET and other Mediana projects.
Build analyzers and test tools are development dependencies.
Packages contain an experimental foundation and the limited MP4/MOV reader described
above. The experimental H.264 and PNG source modules are not packaged. APIs may change before 1.0.

| Package | Responsibility |
| --- | --- |
| Mediana | Public facade and module registration |
| Mediana.Core | Time, packets, frames and buffer ownership |
| Mediana.IO | Byte sources and probe replay |
| Mediana.Containers | Container contracts |
| Mediana.Containers.Mp4 | Optional MP4/MOV metadata and packet reader |
| Mediana.Codecs | Codec contracts |
| Mediana.Processing | SDR-to-sRGB conversion and RGBA32 resizing |
| Mediana.Execution | Sequential video decoding pipeline |

Installing Mediana includes its required modules through NuGet dependencies.
Mediana.Processing and Mediana.Containers.Mp4 are optional separate packages.
All packages share one version.

### Publishing

GitVersion computes the version from Git history and release tags using
[GitVersion.yml](GitVersion.yml). All library packages use the same computed version
for both compilation and packing. Ordinary releases advance the patch version;
minor and major changes require an explicit versioning decision.

Pushes to main run build, formatting, tests and Native AOT checks on Linux, Windows
and macOS. After all checks pass, the workflow publishes the packages and symbols
to NuGet.org, then creates a GitHub release with the matching tag, package assets
and generated release notes. The published artifacts are the packages produced
by the validated build. Pull requests run checks without publishing.

Manual runs validate by default; enable the publish input to publish from main.
Authentication uses [NuGet Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing).
Set the NUGET_USER repository variable to the NuGet profile username. Configure
the matching NuGet policy for repository bvdcode/Mediana, workflow file
publish-release.yml, environment nuget, and package patterns Mediana and Mediana.*
with permission to push new packages and versions.

Re-running a partially completed publication skips existing package versions and
GitHub releases. Package IDs become assigned after successful publication.

## License

[MIT](LICENSE).

# Mediana

Mediana is an MIT-licensed multimedia library under development for .NET 10.
Its media implementation is written in C#, with no FFmpeg, external media processes,
native media libraries, or third-party runtime packages.

The current implementation is experimental. It reads metadata and compressed packets from
non-fragmented MP4/MOV/M4A files containing AVC, AAC or MP3 tracks. The MP4 reader also
exposes movie-level iTunes/indexed metadata and original encoded cover art. The H.264 source module
connects limited CAVLC and CABAC I/P/B decoding to public video sessions and returns cropped YUV frames.
The sample can extract a supported video frame, convert SDR color to sRGB, correct
the sample aspect ratio and right-angle orientation, and save a PNG preview.
An experimental MPEG-TS source module packages AVC/AAC/MP3 access units without decoding.
The WAVE and PCM source modules read integer/float audio through public audio sessions.
The AAC source module decodes limited mono/stereo AAC-LC from MP4/M4A to float PCM
and encodes contiguous PCM as experimental AAC-LC access units.
Independent source modules also decode HEIC/HEIF still images, JPEG 2000 and JBIG2,
and rasterize SVG and TGS/Lottie 2D animation. Their output connects to Mediana
frames, SDR color conversion and PNG encoding. These modules compile inside
Mediana and do not depend on Heic.NET or Render.NET packages.
An experimental PDF module reads page geometry, renders supported vector/text/image
content and exposes decoded image XObjects. See [PDF support and limits](docs/pdf.md).
Experimental fixed-QP H.264 intra encoding and YUV downscaling are available.
Experimental AAC-LC audio encoding to MPEG-TS is also available; perceptual
allocation and transient handling remain open. HLS variants can copy or encode audio.

## Components

| Project | Current responsibility |
| --- | --- |
| `Mediana.Documents.Pdf` | PDF page geometry, managed parsing/rendering and decoded image extraction; source project only |
| `Mediana.Drawing` | Managed 2D paths, fills/strokes, clipping, compositing and RGBA surfaces; source project only |
| `Mediana.Images.Heif` | HEIC item graphs, intra HEVC, grids, transforms and alpha; source project only |
| `Mediana.Codecs.Jpeg2000` | JP2/J2K integer components with native precision and optional RGBA conversion; source project only |
| `Mediana.Codecs.Jbig2` | Packed monochrome pages, globals and optional RGBA expansion; source project only |
| `Mediana.Images.Svg` | SVG paths, styles, text, masks, filters and embedded PNG; source project only |
| `Mediana.Images.Tgs` | TGS/Lottie 2D keyframes and lazy timed raster frames; source project only |
| `Mediana.Core` | Rational time, stream metadata, packets, video/PCM frames, buffer ownership and allocation budgets |
| `Mediana.IO` | Sequential and random access byte sources, stream ownership and probe replay |
| `Mediana.Containers` | Reader/factory contracts and a writer finalization contract |
| `Mediana.Containers.Mp4` | Non-fragmented MP4/MOV/M4A metadata, original cover art and AVC/AAC/MP3 packet reading |
| `Mediana.Containers.Wave` | Sequential RIFF/WAVE integer/float PCM reading; source project only |
| `Mediana.Containers.Aiff` | Sequential mono/stereo AIFF and supported AIFF-C PCM reading; source project only |
| `Mediana.Containers.Flac` | Native FLAC STREAMINFO, Vorbis comments, original pictures and compressed chunks; source project only |
| `Mediana.Containers.Mp3` | MPEG Layer III frames, ID3 text and original pictures, Xing/Info and coded timing; source project only |
| `Mediana.Codecs.Mp3` | MPEG-1/2/2.5 Layer III to float32 PCM, all standard rates, mono/stereo and joint stereo; source project only |
| `Mediana.Containers.MpegTs` | Experimental single-program AVC/AAC/MP3 transport-stream writer; source project only |
| `Mediana.Codecs` | Separate input/output codec protocol and video/audio decoder factories |
| `Mediana.Codecs.Pcm` | Integer/float PCM conversion to native-endian audio frames; source project only |
| `Mediana.Codecs.Flac` | Lossless FLAC PCM, predictors/Rice, stereo decorrelation and frame CRC; source project only |
| `Mediana.Codecs.Aac` | Mono/stereo AAC-LC decoding and experimental long-window PCM encoding; source project only |
| `Mediana.Codecs.H264` | Experimental AVC CAVLC/CABAC I/P/B decoding and fixed-QP Baseline intra encoding; source project only |
| `Mediana.Codecs.Png` | Experimental sRGB RGBA32 PNG encoder; source project only |
| `Mediana.Processing` | SDR conversion to sRGB, preview geometry, PCM conversion, channel matrices, sinc resampling and pull-driven waveform RMS/peak |
| `Mediana.Streaming.Hls` | MP4 stream-copy HLS, streaming AVC variants with copied AAC/MP3 or encoded AAC and measured playlists; source project only |
| `Mediana.Execution` | Pull-driven video/audio decoding, delayed output, bounded container reads and cancellation |
| `Mediana` | Explicit registration, container detection and forward-only video/audio sessions |
| `Mediana.Cli` | Command-line media inspection, conversions, previews and exports |
| `Mediana.Sample` | Minimal generated-frame example using the public API |

Core does not reference the other components. Containers do not reference codecs,
and codecs do not perform I/O. The public facade composes these contracts.

The H.264 work currently reconstructs progressive 8-bit 4:2:0 CAVLC I/P/B
and CABAC I/P/B pictures with default scaling, including motion compensation, reference
picture management, intra prediction, inverse transforms, PCM blocks and deblocking.
Each packet contains a complete picture, with one or more slices in increasing
macroblock order. Three further CAVLC/CABAC multislice sequences match all 54
independent reference pictures, including slice starts inside macroblock rows,
per-slice quantizers, temporal/spatial B prediction and filtering across slice edges.
Two complete Baseline/Main CAVLC sequences and one cropped IDR picture match the
independent filtered YUV references sample for sample (nineteen pictures total).
The Main sequence includes explicit prediction weights and reference-list changes.
Two High CAVLC sequences with 4x4/8x8/16x16 intra prediction, inter 8x8 transforms,
motion, weighted prediction and nonzero deblocking offsets match thirty further
independent YUV pictures internally and through public sessions.
Three High CABAC sequences with the same transform sizes match forty-two further
independent YUV pictures, including all three CABAC initialization selections.
Two High CAVLC I/P/B sequences add 36 complete independent YUV pictures, including
spatial/temporal direct prediction, both reference lists, implicit prediction weights,
reference B pictures and deblocking. Public sessions preserve presentation order;
selected B-picture sRGB PNGs match independent pixels within one channel level.
All 85 CAVLC pictures, six Main CABAC intra pictures and 96 additional CABAC I/P pictures match through MediaEngine.
Five CABAC sequences add 98 complete pictures with B prediction and all initialization
selections. Timing, cropping and sample aspect ratio are preserved. Preview generation requires
the supported CAVLC/CABAC I/P/B subset and known color and sample aspect ratio;
it does not cover general H.264 files.

## Images and 2D animation

The image modules use their own document/component APIs instead of inventing
video tracks for HEIF items, JBIG2 pages or vector scenes. Raster results expose
`ToVideoFrame` for the existing processing and PNG APIs. HEIC and JPEG 2000 retain
native color information; conversion rejects ICC profiles instead of labelling
them sRGB. HDR tone mapping is not implemented. JPEG 2000 native component
precision remains available separately from its optional RGBA8 preview.

```sh
dotnet run --project tools/Mediana.Cli -c Release -- image heic photo.heic preview.png --aspect-width 1 --aspect-height 1
dotnet run --project tools/Mediana.Cli -c Release -- image jp2 scan.jp2 preview.png --aspect-width 1 --aspect-height 1
dotnet run --project tools/Mediana.Cli -c Release -- image jbig2 scan.jb2 preview.png --aspect-width 1 --aspect-height 1
dotnet run --project tools/Mediana.Cli -c Release -- image svg artwork.svg preview.png --width 512
dotnet run --project tools/Mediana.Cli -c Release -- image tgs animation.tgs preview.png --time 0.5 --width 512
```

Raster examples select the primary HEIC item or first JBIG2 page. The final two
arguments explicitly declare pixel aspect ratio; `1 1` means square pixels.
The APIs also expose item selection, all JBIG2 pages and native JPEG 2000 samples.
SVG/TGS width preserves the viewport proportions; TGS time is seconds from its
in-point. Existing output files are never overwritten. Input, output and
workspace have separate budgets; the output allocator is not a whole-process
memory limit.

Supported subsets and limits: [HEIC](docs/heif.md),
[JPEG 2000](docs/jpeg2000.md), [JBIG2](docs/jbig2.md),
[SVG](docs/svg.md), [TGS/Lottie](docs/tgs.md).
TGS/Lottie expressions are explicitly unsupported. Standalone PNG decoding,
PDF page rendering supports the subset described below. 3D file formats are outside
the library's scope.

## Download and run

Each publishing run attaches standalone Native AOT command-line executables to the
[GitHub release](https://github.com/bvdcode/Mediana/releases). Download the executable
for your operating system and processor. No .NET SDK, .NET runtime or FFmpeg
installation is needed.

| Download | Platform |
| --- | --- |
| `Mediana-<version>-linux-x64` | Linux x64 (glibc), built on Ubuntu 24.04 |
| `Mediana-<version>-win-x64.exe` | Windows x64 |
| `Mediana-<version>-osx-arm64` | macOS on Apple Silicon, built on macOS 15 |

Rename the downloaded file to `mediana` (`mediana.exe` on Windows).
On Linux/macOS, make it executable with `chmod +x mediana`.

Optional archives include the same executable, this README and the MIT license:

| Archive | Platform | Executable |
| --- | --- | --- |
| `Mediana-<version>-linux-x64.tar.gz` | Linux x64 (glibc), built on Ubuntu 24.04 | `mediana` |
| `Mediana-<version>-win-x64.zip` | Windows x64 | `mediana.exe` |
| `Mediana-<version>-osx-arm64.tar.gz` | macOS on Apple Silicon, built on macOS 15 | `mediana` |

Each executable and archive has a matching `.sha256` checksum file. For Linux,
use Ubuntu 24.04 or a compatible newer glibc distribution; the executable does not
target Alpine.

From the download or extraction directory on Linux/macOS:

```sh
./mediana --help
./mediana -i audio.mp3 out.wav
./mediana -i photo.heic out.png
./mediana -i artwork.svg out.png
./mediana -i report.pdf page.png --page 1
./mediana -i input.mp4 frame.png --time-ms 250
./mediana -i input.mp4 output.ts
./mediana -i input.mp4 encoded.ts --video-codec H264 --video-only --qp 26
./mediana -i input.mp4 small.ts --video-codec H264 --video-only --width 640 --height 360
./mediana -i input.mp3 audio.ts --audio-codec Aac --audio-bit-rate 128000
./mediana probe mp4 input.mp4
./mediana hls create input.mp4 output-hls --audio-stream 1
```

On Windows, use `./mediana.exe` in PowerShell with the same arguments.
The MP4 probe lists stream indices; use the appropriate video/audio indices in
the HLS, preview and audio commands. Omit `--audio-stream` for `hls create` without
audio; use explicit `--video-only` for `hls encode` without audio. Output paths
must be new, and their parent directories must already exist.

MP4-to-TS/HLS copies supported AVC/AAC/MP3 streams without re-encoding. PCM export
supports WAVE/AIFF PCM, FLAC, MP3 and the supported AAC-LC subset in MP4/M4A. It writes raw,
native-endian interleaved samples without a WAVE header and prints the sample
format, channel layout and sample rate. Video previews require the supported
H.264 subset described above. An explicit `--video-codec H264 --video-only` selects
fixed-QP intra video encoding to TS. `--audio-codec Aac` selects audio-only AAC-LC
encoding to TS; see [audio encoding contracts and limits](docs/aac-encoding.md).
The command-line arguments match the source examples below: replace
`dotnet run --project tools/Mediana.Cli -c Release --` with the executable.

The main command is `mediana -i input output`. Input content determines its
format; the output extension selects WAVE PCM16 (`.wav` or `.wave`), PNG (`.png`) or
MPEG-TS (`.ts`, stream copy by default). WAVE conversion preserves sample rate and channel
order and performs no resampling or dithering. RIFF output must fit its 32-bit
size fields; RF64 is not supported. Named multichannel audio must follow WAVE
speaker order. PNG output is one image, a video frame at or after `--time-ms`
(default 0), an animation rendered at that time, or a PDF `--page` (default 1). Vector and animation
images use intrinsic dimensions; PDF pages render at 96 DPI. The existing format
subsets described below also apply to these commands. Unsupported output formats
fail explicitly.

Every invocation first prints the product version and copyright in stderr.
Commands have typed options and generated help. Use `mediana --help` to list
commands and, for example, `mediana audio decode --help` for that command's options.
Add `--log-level Debug` after the command to include diagnostic details, or
`--log-level None` to disable diagnostics. Logs go to stderr; command results go
to stdout. Colors are automatic in an interactive terminal; `--no-color` or the
`NO_COLOR` environment variable disables them.
WAVE and AAC conversion report processed media time, speed relative to playback
and output size immediately, at one-second intervals, and on completion. When
`EncodedStreamInfo.CodedDuration` is known, a shared media-time estimator adds
`eta=hh:mm:ss` after a short warmup using smoothed recent processing speed.
The total comes from container metadata, including codec delay and padding; it
does not apply presentation edits. MP3 Xing/Info, WAVE, AIFF and FLAC with a known
sample count provide this metadata. Unknown totals, insufficient progress, stalls
and completion omit the ETA field. No extra file scan or bitrate estimate is used.

Ctrl+C cancels processing and removes incomplete output. Existing destinations
are never replaced. Exit codes are 0 for success, 1 for an unexpected failure,
2 for invalid arguments, 3 for an unsupported operation or format feature,
4 for invalid media data, 5 for file access errors, and 130 for cancellation.

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
dotnet run --project tools/Mediana.Cli -c Release --no-build -- probe mp4 tests/Mediana.Tests/Data/Mp4/avc-aac.mp4
dotnet run --project tools/Mediana.Cli -c Release --no-build -- cover mp4 tests/Mediana.Tests/Data/Metadata/covers.m4a artifacts/cover.png --index 0
dotnet run --project tools/Mediana.Cli -c Release --no-build -- audio decode tests/Mediana.Tests/Data/Wave/s24-surround.wav artifacts/surround.pcm --stream 0
dotnet run --project tools/Mediana.Cli -c Release --no-build -- audio resample tests/Mediana.Tests/Data/Wave/s16-stereo.wav artifacts/mono-400.s16le --stream 0 --sample-rate 400 --gains 0.5 0.5
dotnet run --project tools/Mediana.Cli -c Release --no-build -- video preview tests/Mediana.Tests/Data/Mp4/avc-weighted.mp4 artifacts/preview.png --stream 0 --time-ms 250 --width 100 --height 100
dotnet run --project tools/Mediana.Cli -c Release --no-build -- video preview tests/Mediana.Tests/Data/Mp4/avc-weighted-rotated.mp4 artifacts/preview-rotated.png --stream 0 --time-ms 0 --width 100 --height 100
dotnet run --project tools/Mediana.Cli -c Release --no-build -- video preview tests/Mediana.Tests/Data/Mp4/avc-cabac-intra.mp4 artifacts/preview-cabac.png --stream 0 --time-ms 450 --width 100 --height 100
dotnet run --project tools/Mediana.Cli -c Release --no-build -- video preview tests/Mediana.Tests/Data/Mp4/avc-cabac-p2.mp4 artifacts/preview-cabac-p.png --stream 0 --time-ms 450 --width 100 --height 100
dotnet run --project tools/Mediana.Cli -c Release --no-build -- video preview tests/Mediana.Tests/Data/Mp4/avc-cavlc-8x8-weighted.mp4 artifacts/preview-high-cavlc.png --stream 0 --time-ms 450 --width 100 --height 100
dotnet run --project tools/Mediana.Cli -c Release --no-build -- video preview tests/Mediana.Tests/Data/Mp4/avc-cabac-8x8-2.mp4 artifacts/preview-high-cabac.png --stream 0 --time-ms 450 --width 100 --height 100
```

The self-test decodes a generated 2×2 RGBA packet through the codec protocol, resizes
it to 1×1, checks the red output pixel and exact time conversion, then verifies
that all leased buffer bytes have been released. It does not open a video file.

The preview command takes input, a new output path, video stream index, target
`--time-ms` in the media timeline, `--width` and `--height`. In this example,
it selects the first frame at or after 250 ms (300 ms) and writes a 100×49 sRGB PNG
with square pixels. The output directory must exist. Existing files are not replaced;
a request beyond the last frame fails without creating an image.
The rotated fixture writes 49×100 after applying its display matrix and SAR.
The CABAC intra fixture selects 600 ms for a request at 450 ms and writes 100×49.
The CABAC P fixture selects 500 ms for the same request and writes 100×49.
The High CAVLC and CABAC 8x8 fixtures select 500 ms and write 100×50.

Publish and execute it with Native AOT on Linux x64:

```sh
dotnet publish tools/Mediana.Cli -c Release -r linux-x64 -m:1 -p:IlcSingleThreaded=true -p:Version="$PACKAGE_VERSION" -o artifacts/native/linux-x64
./artifacts/native/linux-x64/Mediana.Cli --help
./artifacts/native/linux-x64/Mediana.Cli probe mp4 tests/Mediana.Tests/Data/Mp4/avc-aac.mp4
./artifacts/native/linux-x64/Mediana.Cli audio decode tests/Mediana.Tests/Data/Wave/s16-stereo.wav artifacts/audio-native.pcm --stream 0
./artifacts/native/linux-x64/Mediana.Cli video preview tests/Mediana.Tests/Data/Mp4/avc-weighted.mp4 artifacts/preview-native.png --stream 0 --time-ms 250 --width 100 --height 100
dotnet publish tests/Mediana.NativeTests -c Release -r linux-x64 -m:1 -p:IlcSingleThreaded=true -p:Version="$PACKAGE_VERSION" -o artifacts/native-tests/linux-x64
./artifacts/native-tests/linux-x64/Mediana.NativeTests
```

Native AOT requires the platform build toolchain. The resulting executable targets
one OS/architecture and still uses standard OS runtime facilities.
See the [.NET Native AOT prerequisites](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/).

Mediana.NativeTests executes internal H.264 CAVLC/CABAC I/P/B reconstruction and public MediaEngine
video sessions. Each path compares all visible YUV samples in 85 CAVLC pictures
with the checked-in references. Public sessions also compare six CABAC intra pictures and 96 CABAC I/P pictures
and verify timing, cropping,
sample aspect ratio and allocation release. Its fixture loading and comparison code
is shared with the unit tests. Indexed video seeking is checked against six
independent YUV sequences, including backward targets, EOF restart and retained
frames. The native checks also compare SDR conversion with
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
The public decoder reconstructs supported CABAC I/P/B pictures, including weighted
prediction, multiple references and per-partition motion differences. Four complete
CABAC I/P sequences match 54 independent YUV pictures with all three cabac_init_idc
selections, mixed intra/inter prediction and cropped storage.
Eight-bit CAVLC and CABAC support 8x8 intra prediction/transforms; all nine
directional prediction modes match explicit independent vectors. Three High CABAC
sequences match 42 additional complete I/P pictures with actual intra/inter 8x8
use. The 34 progressive 8x8 contexts match all 7072 normative initial states;
residual intervals exercise every scan position, signs and large magnitudes.
CAVLC B reconstruction covers all macroblock and sub-macroblock types, spatial and
temporal direct prediction, both reference lists and deblocking. Two real sequences
verify all 36 reordered pictures and selected B-picture PNG previews. Five CABAC
sequences verify another 98 complete pictures, including all three initialization
selections, actual B-picture 8x8 transforms, spatial/temporal direct, reference B
pictures, public presentation timing and selected B-picture PNG previews.
B-specific contexts match all 2496 normative initial states; exact intervals
exercise all 49 macroblock types, 13 sub-macroblock types and independent L0/L1
reference-index and motion-difference state. Broader slice support remains open.
Unsupported or damaged pictures fault the session
and release pending output; reset or disposal is required. Unused bits
in the arithmetic terminal byte are discarded; following zero words are checked.
This decoder is not a complete bitstream conformance validator.
Seven complete WAVE sequences also match independent PCM references through public
audio sessions with a 16 KiB media-buffer budget, including unsigned/signed integers,
both float precisions and six-channel speaker order. Returned audio frames retain
their storage after session disposal. CI is configured to build and run both native
executables on Linux, Windows and macOS.

## Reading WAVE PCM

Reference the `Mediana.Containers.Wave` and `Mediana.Codecs.Pcm` source projects:

```csharp
MediaEngine engine = new MediaEngineBuilder()
    .AddContainer(new WaveReaderFactory())
    .AddAudioDecoder(new PcmDecoderFactory())
    .Build();
await using RandomAccessStreamByteSource source = new(File.OpenRead(path));
await using AudioSession session = await engine.OpenAudioAsync(source, 0);
using AudioFrame? frame = await session.ReadFrameAsync();
```

The namespaces are Mediana, Mediana.Codecs.Pcm, Mediana.Containers.Wave,
Mediana.Core.Audio and Mediana.IO. Sequential byte sources are also supported.
Sessions borrow the source, own decoder/reader state, enforce cancellation and
configured resource limits, and return independently owned PCM frames. Frame PTS
and duration use exact sample counts. Random-access opening preserves the source cursor.

The reader supports one little-endian RIFF/WAVE `data` chunk, preceded by its
`fmt ` configuration. Integer PCM accepts 8/16/24/32-bit storage; IEEE float accepts
32/64-bit storage. The shared `PcmConfiguration` describes standard WAVEFORMATEX
or WAVEFORMATEXTENSIBLE bytes, including valid precision and explicit channel order.
The decoder returns interleaved native-endian samples. Signed 24-bit input expands
to left-aligned signed 32-bit output; float bit patterns retain their precision.
Channel masks preserve the declared speaker order; unassigned channels remain discrete.

Metadata chunks are skipped within byte/chunk limits. Trailing chunks and padding
are checked when the stream is drained. RF64, RIFX, compressed WAVE, nested audio
lists and tag/artwork extraction are unsupported.
The `audio decode` command takes input, a new raw PCM output path and `--stream`;
it reports the sample format, rate and channels needed to interpret its output.
It does not write a WAVE header. The output directory must exist, and existing
files are not replaced. See the [WAVE fixtures](tests/Mediana.Tests/Data/Wave/README.md)
for independent references and format definitions.

## Reading AIFF audio

`Mediana.Containers.Aiff` reads one `SSND` chunk from AIFF or AIFF-C, with exact
sample counts and timestamps. Add `AiffReaderFactory` to the container registry
and `AiffPcmDecoderFactory` from `Mediana.Codecs.Pcm` to the audio decoder registry.
The decoder shares PCM conversion with the WAVE path. Its `pcm.aiff` configuration
contains the four-byte FORM type followed by the complete `COMM` payload.

Supported input is mono/stereo signed integer AIFF with 1–32 bits of precision,
AIFF-C `NONE`, 16-bit `sowt`/`twos`, and big-endian `fl32`/`fl64`. Decoded integer
storage is native-endian and left-aligned; signed 8-bit input becomes unsigned
8-bit PCM. A positive integral sample rate must fit `AudioFormat`.

The reader borrows the source, streams bounded packets and honors `SSND` offsets
and trailing block fill. `COMM` and the AIFF-C version chunk must precede `SSND`;
other chunk orders requiring a backwards read are unsupported. Unknown metadata
chunks are skipped within byte/count limits. Tags, artwork, multichannel layouts,
fractional sample rates and compressed AIFF-C codecs are not supported.
Trailing chunks and padding are checked when the stream is drained.

The CLI detects AIFF by content for WAVE conversion, raw PCM, resampling and
waveform data:

```sh
mediana -i recording.aiff output.wav
mediana audio waveform recording.aiff waveform.json --gains 0.5 0.5
```

[AIFF references](tests/Mediana.Tests/Data/Aiff/README.md) cover complete integer
and float sequences through the public session, including sample timing,
independent frame ownership and a 16 KiB media-buffer budget.

## Decoding AAC-LC

Reference `Mediana.Containers.Mp4` and the `Mediana.Codecs.Aac` source project:

```csharp
MediaEngine engine = new MediaEngineBuilder()
    .AddContainer(new Mp4ReaderFactory())
    .AddAudioDecoder(new AacDecoderFactory())
    .Build();
await using RandomAccessStreamByteSource source = new(File.OpenRead(path));
await using AudioSession session = await engine.OpenAudioAsync(source, audioStreamIndex);
using AudioFrame? frame = await session.ReadFrameAsync();
```

The decoder accepts AAC-LC with indexed sample rates from 8000 to 96000 Hz,
standard mono/stereo channel configuration and one `raw_data_block` per packet.
It returns 1024 native-endian interleaved float32 samples per channel per frame.
Mono is `front-center`; stereo is `front-left`, `front-right`. Packet PTS is
preserved, and frame duration is exactly 1024 samples. The decoder preserves
encoder delay and final padding. The final PCM count can therefore exceed the
original recording length. Explicit sample selection is available after decoding.

All eleven spectral codebooks, escape values, scalefactors, pulse data, long/short
sine/KBD windows, TNS, perceptual noise substitution, M/S and intensity stereo
are implemented. HE-AAC/SBR, spatial extensions, dynamic-range extensions,
coupling/LFE/program-configuration elements, multichannel layouts, 960-sample
frames and 7350 Hz are unsupported. The module does not read ADTS or ADIF files.
Unsupported tools and malformed input fail explicitly; failure or cancellation
requires reset or disposal. Reset clears overlap and noise state, so continuous
output requires decoding the preceding packets. The default access-unit byte
limit is 8192 and can be configured in the factory.

Three complete real M4A sequences run through the same managed and Linux Native
AOT checks with a 128 KiB media-buffer budget. Two PNS-disabled sequences match
independent float PCM within `2e-6` for every sample (observed maximum error
`8.94e-8`). The PNS-enabled sequence uses channel/block and total-energy checks
because random sequences differ between decoders. Tests also verify all four
window sequences, both shapes, TNS, joint stereo, every PTS, retained-frame
ownership, cancellation, reset and partial allocation failure. Numeric tables and
equations follow ISO/IEC 13818-7:2004 and ISO/IEC 14496-3:2009; see the
[AAC references](tests/Mediana.Tests/Data/Aac/README.md) for provenance and limits.

Two complete sequences also pass through explicit mono mixing, 400 Hz sinc
resampling, PCM16 quantization and streaming RMS/peak in managed and Linux Native
AOT execution. Every resulting PCM16 sample differs by at most one from processing
the independently decoded float reference, with matching sample counts, PTS,
durations and final partial level windows. This isolates the effect of AAC
decoding. A separate comparison with Cotton's waveform renderer is described below.

The same `audio decode` and `audio resample` commands accept supported M4A
inputs. Select the audio stream index explicitly:

```sh
dotnet run --project tools/Mediana.Cli -c Release --no-build -- audio decode tests/Mediana.Tests/Data/Aac/lc-stereo-44.m4a artifacts/aac.f32le --stream 0
dotnet run --project tools/Mediana.Cli -c Release --no-build -- audio resample tests/Mediana.Tests/Data/Aac/lc-stereo-44.m4a artifacts/aac-mono-400.s16le --stream 0 --sample-rate 400 --gains 0.5 0.5
```

## Processing PCM

The `Mediana.Processing.Audio` namespace provides independent processing steps.
`AudioTrimmer` skips a caller-specified number of decoded samples and optionally
selects an exact sample count. It pulls owned frames, copies only the selected
parts into owned output and keeps no recording-sized history. Interleaved and
planar PCM retain their format, channel order and exact sample bytes. A null count
reads through EOF; a finite requested range must exist completely. A zero count
returns EOF without reading. Original timestamps remain continuous unless the
caller supplies an explicit output origin. It stops reading at the selected end;
the source remains borrowed and must still be disposed by its owner.

`Mp4AudioSampleRange.FromEditList(track, decodedSampleRate)` translates one
explicit unit-rate media edit, optionally preceded by an empty edit, into a PCM
range and the first sample's movie presentation time. It selects sample instants
in the half-open edit interval using exact rational arithmetic. Leading empty
edits shift the returned PTS without generating silent PCM. Missing or nonlinear
edits and unsupported exact clocks fail explicitly; codec delay and roll groups
are not inferred. Range indices count from media time zero, so decode preceding
packets from that point before applying the range; arbitrary seeking needs
separate decoder preroll handling.

```csharp
Mp4AudioSampleRange range = Mp4AudioSampleRange.FromEditList(track, decodedSampleRate);
await using AudioTrimmer trimmer = new(session.ReadFrameAsync,
    range.StartSample, range.SampleCount, allocator, range.PresentationTime);
using AudioFrame? frame = await trimmer.ReadFrameAsync();
```

Pass `null` instead of `range.SampleCount` to remove only the leading samples.
These policies differ: on the stereo AAC reference, ordinary FFmpeg decoding
returns 16384 samples after removing priming, while the exact edit selects 16317.
Both complete results are checked against independent decoding in managed and
Linux Native AOT execution. The exact edit also feeds the complete mono 400 Hz
PCM16 and RMS pipeline; its samples and levels match processing of the independently
decoded and cropped reference, including both edges. Edit-list time is distinct from AAC frame duration;
see Apple's [explicit encoder-delay representation](https://developer.apple.com/documentation/quicktime-file-format/using_track_structures_to_represent_encode_delay_explictly).

`AudioChannelMixer` takes an explicit output-by-input coefficient matrix and channel
labels. It preserves finite headroom in interleaved float64 output, without implicit
speaker mapping, gain normalization or clipping. `AudioSampleConverter` changes
native-endian sample representation and planar/interleaved storage. Integer output
saturates and rounds to nearest even without dithering; processing rejects NaN,
infinity and values that exceed finite target float storage. Both operations borrow
the input frame and return an independently owned frame with preserved timing.

`AudioLevelAccumulator` emits fixed-size mono RMS/peak windows with sample counts,
PTS and duration. Windows span input frame boundaries; `Complete` emits the final
partial window. A scaled energy sum supports finite floating-point headroom without
squaring overflow or underflow. Frames are borrowed, samples are not retained, and
levels are passed by value to a synchronous callback. Emitted windows are a prefix
if processing or the callback fails. Display normalization, tapering and waveform
rendering belong to the application.

`AudioWaveformReader` combines an explicit mono mix, resampling, optional sample
quantization and level accumulation behind `ReadLevelAsync`. Each call returns
one `AudioLevel`, or null at EOF. `AudioWaveformOptions` requires the target rate
and window size; its sample format defaults to float64, preserving finite
headroom. Selecting `Signed16` applies saturation and PCM16 quantization before
computing RMS/peak. The final partial window is returned with its exact count,
timestamp and duration. Returned levels contain no frame leases.

```csharp
await using AudioWaveformReader waveform = new(
    session.ReadFrameAsync, new double[] { 0.5, 0.5 },
    new AudioWaveformOptions(400, 40)
    {
        SampleFormat = AudioSampleFormat.Signed16,
        Resampling = new AudioResampleOptions
        {
            CutoffRatio = 0.97,
            BoundaryMode = AudioBoundaryMode.Reflect
        }
    }, allocator);
while (await waveform.ReadLevelAsync(cancellationToken) is AudioLevel level)
{
    ConsumeLevel(level);
}
```

The reader owns callback frames and filter storage, borrows the source and uses
the caller's media-buffer allocator. It keeps bounded processing history and one
pending level, independent of recording length. EOF, failure, cancellation and
disposal release owned storage. Failure or cancellation makes the reader terminal;
concurrent reads/disposal fail explicitly. It infers neither channel mapping nor
codec priming. To select a decoded range, pass `AudioTrimmer.ReadFrameAsync` as the
callback. Display shaping remains in the application. Managed and Linux Native
AOT checks cover independent WAVE/speech references and raw/exact-edited AAC.

`AudioResampler` pulls owned frames from an asynchronous callback and returns
interleaved float64. It uses a Blackman-windowed sinc low-pass filter before rate
conversion, a polyphase coefficient bank, bounded sample history and exact rational
positions. Cutoff, filter support, output block size and coefficient-byte limits are
configurable. The caller supplies an allocation budget. The input format and PTS
must remain continuous; unknown timestamps remain unknown. Filter lookahead delays
availability, while output timestamps retain the original timeline. `BoundaryMode`
defaults to `Zero`; `Reflect` mirrors sample positions around the first and last
sample instants without repeating endpoints. A single-sample recording repeats
that sample. Both modes use the same bounded history. Output contains
`ceil(input samples * output rate / input rate)` samples. Equal rates preserve the
samples without filtering.
The rate-conversion basis is described in
[Windowed Sinc Interpolation](https://www.dsprelated.com/freebooks/pasp/Windowed_Sinc_Interpolation.html).

The resampler owns frames returned by the callback and releases its history on drain,
failure or disposal. It borrows the callback's source; returned output frames survive
resampler disposal. Concurrent operations, discontinuities and unsupported exact
clock/filter dimensions fail explicitly. Managed and Linux Native AOT checks cover
tone amplitude, independent channels, exact counts/timestamps and output ownership;
managed checks also cover stop-band rejection, packet boundary invariance, early
disposal, cancellation, resource limits and invalid input.

The `audio resample` command takes input, a new output path, `--stream`,
`--sample-rate` and space-separated `--gains` in source-channel order. It mixes to mono,
resamples and streams signed 16-bit little-endian PCM with no header. For stereo,
`--gains 0.5 0.5` selects an average; multichannel gains must be supplied explicitly. This
provides the PCM shape needed for a 400 Hz waveform. The complete mono mix →
resample → PCM16 → RMS path is checked on a real WAVE input and analytic tone energy
in managed and Linux Native AOT execution. AAC-LC now supplies compressed input
for the same processing steps. Other compressed audio, cover art in other
containers and Cotton integration remain open.

An explicit `CutoffRatio = 0.97`, `BoundaryMode = Reflect` profile is checked
against independent mono 400 Hz PCM16 references for silence, endpoint impulses,
a synthetic musical signal, recorded speech and a fractional-duration recording.
Every sample, timestamp and RMS/peak window is checked in managed and Linux
Native AOT execution; the [corpus documentation](tests/Mediana.Tests/Data/Waveform/README.md)
states the numeric bounds and speech attribution. General defaults remain 0.94
and zero extension.

A local comparison passed these five WAVE inputs and three AAC-LC M4A inputs
through Cotton's existing 150-pixel waveform renderer. For AAC it removed only
the explicit edit's leading samples, preserving final decoded padding to match
the existing application policy. Mediana's output had the same PCM length on all
eight inputs. The maximum sample difference was 34 PCM16 steps; alpha-silhouette
intersection over union ranged from 99.64% to 100%. The comparison supplied
Mediana PCM through a 400 Hz WAVE wrapper and independently verified byte identity
through the renderer's existing PCM decode step. Lossy WebP pixels differ; these
short inputs establish neither bit-identical rendering nor general media
equivalence. Application integration and the policy for the final remainder of
samples beyond Cotton's equal bar windows remain open.

The sample exports waveform data directly from supported WAVE, AAC-LC or native FLAC sources:

```bash
dotnet run --project tools/Mediana.Cli -c Release --no-build -- audio waveform tests/Mediana.Tests/Data/Waveform/speech.wav artifacts/speech-waveform.json --stream 0 --sample-rate 400 --samples-per-window 40 --gains 1
dotnet run --project tools/Mediana.Cli -c Release --no-build -- audio waveform tests/Mediana.Tests/Data/Aac/lc-stereo-44.m4a artifacts/aac-waveform.json --stream 0 --sample-rate 400 --samples-per-window 16 --gains 0.5 0.5
```

Arguments are input, a new JSON output, stream index, target rate, samples per
window and gains in source-channel order. The sample explicitly selects PCM16,
cutoff 0.97 and reflected boundaries. JSON includes RMS/peak, counts and rational
timestamps/durations; periodic flushes bound writer buffering. Output is published
only after successful completion. Existing destinations are preserved and failed
processing removes the temporary output. Like the PCM-export samples, it retains
complete decoded AAC frames, including priming/padding; application edit selection
is available through the library API. The same command runs with the Native AOT
sample executable.

Managed and Native AOT JSON outputs match on recorded speech and stereo AAC.
A local Linux x64 Native AOT run processed a generated three-minute 48 kHz stereo
WAVE into 72000 mono samples and 1800 level windows in 2.01 seconds, with an
observed peak RSS of 13056 KiB. All level counts/times matched an independent PCM
reference; maximum RMS error was `5.10e-6` and peak error one PCM16 step. This is
one resource observation, not a controlled throughput comparison.

## Reading MP3 metadata, frames and pictures

The source-only `Mediana.Containers.Mp3` module reads MPEG-1/2/2.5 Layer III
files from a sequential source, including short reads and unknown source length.
It reads one leading ID3v2.3/v2.4 tag, text and original APIC picture bytes,
Xing/Info declarations and a final ID3v1 tag. The sample exposes:

```sh
dotnet run --project tools/Mediana.Cli -c Release -- probe mp3 input.mp3
dotnet run --project tools/Mediana.Cli -c Release -- cover mp3 input.mp3 cover.png --index 0
```

`Mp3Reader.OpenAsync` borrows the input. `ReadPacketAsync` returns one complete
Layer III frame with its own lease and continuous coded-sample PTS/DTS/duration;
Xing/Info is excluded from audio packets. Bitrate and padding may vary within a
stream. Frames may depend on previous main data through the bit reservoir, so
`IsKeyFrame` is unspecified. Coded timing includes codec delay and padding;
`CodedDuration` is declared by Xing/Info or available after a complete read.
The declared frame count is checked at EOF. `ByteCount` remains descriptive.

`TextFrames` preserves frame IDs, TXXX descriptions and values; v2.4 supports
multiple values. `Pictures` describes APIC bytes, and `ReadPicture` returns a
lease that survives reader disposal. Linked-picture URLs are returned as bytes
and never retrieved. `TrailingTag` becomes available after EOF and is kept
separate from ID3v2 text. The metadata probe does not scan the full file.

Metadata/frame sizes and counts are bounded. Unsynchronisation, grouping,
v2.4 data-length indicators and plain extended headers are handled. Compressed
or encrypted ID3 frames, extended CRC/update/restriction fields, ID3v2.2,
VBRI, free-format frame discovery, junk resynchronization, stream format changes,
seeking, MPEG CRC verification and gapless interpretation are not supported.
The decoder below verifies MPEG CRCs and enables PCM and waveform export. MP3
encoding and MP3 transport-stream writing remain unsupported.

The [MP3 reference corpus](tests/Mediana.Tests/Data/Mp3/README.md) verifies every
packet's bytes and coded timing, text, picture contents, trailing tags and
ownership against independent evidence for MPEG-1/2/2.5, CBR/VBR and both ID3
versions. The speech fixture has separate CC BY 4.0 attribution.

## Decoding MP3 audio

Reference the `Mediana.Containers.Mp3` and `Mediana.Codecs.Mp3` source projects:

```csharp
MediaEngine engine = new MediaEngineBuilder()
    .AddContainer(new Mp3ReaderFactory())
    .AddAudioDecoder(new Mp3DecoderFactory())
    .Build();
await using AudioSession session = await engine.OpenAudioAsync(source, 0);
```

The decoder restores MPEG-1/2/2.5 Layer III at all nine standard sample rates,
mono, stereo, dual channel, intensity stereo and mid/side stereo. It handles the
bit reservoir, all Huffman tables, long/start/short/stop and mixed windows,
alias reduction, overlap-add and polyphase synthesis. Output is native-endian,
interleaved float32 PCM, with 1152 samples per MPEG-1 frame or 576 for MPEG-2/2.5.
Timestamps follow the input packets. Coded encoder delay and end padding remain;
LAME gapless trimming is not applied.

`Mp3DecoderFactory(maxFrameBytes)` bounds each complete encoded frame (4096 bytes
by default). Configuration contains the first four-byte header. Protected fields
are CRC-checked. The allocator accounts for reservoir, spectrum, synthesis history
and output storage. Retained output frames survive reset/disposal. Failure or
cancellation requires reset or disposal; reset clears all decoding history.
Seeking into reservoir-dependent frames requires decoding the preceding data.
Free-format input, format changes, de-emphasis and encoding are unsupported.

```bash
dotnet run --project tools/Mediana.Cli -c Release --no-build -- audio decode input.mp3 output.f32le --stream 0
dotnet run --project tools/Mediana.Cli -c Release --no-build -- audio waveform input.mp3 waveform.json --stream 0 --sample-rate 400 --samples-per-window 10 --gains 0.5 0.5
```

The [PCM corpus](tests/Mediana.Tests/Data/Mp3/README.md#pcm-decoding) compares
complete real files and constructed all-rate stereo/window vectors with independent
sample references in managed execution and Native AOT. It documents the distinct
reference treatment of 8 kHz mixed windows and illegal low-rate intensity positions.

## Reading native FLAC metadata and pictures

The source `Mediana.Containers.Flac` module reads native files beginning with
`fLaC`. STREAMINFO supplies rate, channels, precision, block/frame bounds, total
samples and the stored decoded-audio MD5. A zero total sample count has unknown
duration. `FlacConfiguration` preserves these declarations without decoding PCM
or verifying the MD5.

```csharp
MediaBufferAllocator allocator = new(32 * 1024 * 1024);
await using StreamByteSource source = new(File.OpenRead(path));
await using FlacReader reader = await FlacReader.OpenAsync(source, allocator);
IReadOnlyList<FlacComment> comments = reader.Comments;
IReadOnlyList<FlacPictureInfo> pictures = reader.Pictures;
using MediaBuffer picture = reader.ReadPicture(0);
await output.WriteAsync(picture.Memory);
```

The namespaces are `Mediana.Containers.Flac`, `Mediana.Core.Memory` and
`Mediana.IO`. The reader consumes the source from its current position and
borrows it through disposal. Comment spelling, UTF-8 values, repeated names and
file order remain available; compare comment names with `OrdinalIgnoreCase`.
Text decoding is strict. Original picture bytes have independent leases that
survive reader disposal and account for the complete picture metadata block.
Picture dimensions and media types are declarations, not image validation.
Media type `-->` exposes URI bytes without retrieving the resource.

`ReadPacketAsync` streams bounded compressed byte chunks in file order. They
have arbitrary boundaries and no frame timestamps/durations or key-frame flag;
a FLAC codec must recover actual frames and sample timing. The reader validates
metadata structure, block uniqueness, padding and seek-table entry ordering.
Application blocks and cuesheets are skipped; they are not interpreted. Limits
bound metadata bytes/blocks, individual text values, comments, pictures and packet
size. Media bytes use the supplied allocator. Cancellation/failure is terminal
and releases owned storage; concurrent operations/disposal fail explicitly.

Native metadata, exact compressed bytes and original PNG/JPEG pictures match
independent references on stereo musical and recorded-speech files, with 127- and
16384-byte chunk limits; Linux Native AOT executes the same verifier with 127-byte
chunks and a 128 KiB media-buffer budget. Parser checks include one-byte source reads, Unicode,
repeated comments, linked pictures, retained leases, malformed data, configured
limits, cancellation and concurrency. Source attribution and exact commands are
in the [FLAC reference documentation](tests/Mediana.Tests/Data/Flac/README.md).
The original implementation follows [RFC 9639](https://www.rfc-editor.org/rfc/rfc9639.html).

```bash
dotnet run --project tools/Mediana.Cli -c Release --no-build -- probe flac tests/Mediana.Tests/Data/Flac/stereo-covers.flac
dotnet run --project tools/Mediana.Cli -c Release --no-build -- cover flac tests/Mediana.Tests/Data/Flac/stereo-covers.flac artifacts/flac-cover.png --index 0
```

The probe reads metadata only. Cover extraction preserves an existing output and
publishes original embedded bytes after successful processing; URI pictures are
rejected by that sample. The source module is not packaged. Decoded-audio MD5
verification, seeking, encoding, custom channel masks,
reserved metadata block types and Ogg/MP4 FLAC mapping remain unsupported.
The managed and Linux Native AOT samples extract the original reference PNG/JPEG
bytes exactly and release all retained media buffers.

## Decoding native FLAC audio

Reference the `Mediana.Containers.Flac` and `Mediana.Codecs.Flac` source projects:

```csharp
MediaEngine engine = new MediaEngineBuilder()
    .AddContainer(new FlacReaderFactory())
    .AddAudioDecoder(new FlacDecoderFactory())
    .Build();
await using AudioSession session = await engine.OpenAudioAsync(source, 0);
```

The decoder accepts contiguous encoded chunks with arbitrary boundaries, starting
at the first audio frame. It restores constant/verbatim subframes, fixed predictors
of orders 0–4, LPC orders 1–32, partitioned Rice4/Rice5 and escaped residuals,
wasted bits, independent channels and all three stereo decorrelation modes.
It uses wide integer arithmetic for predictions and the 33-bit stereo side channel.
PCM is native-endian, interleaved `Signed32`, with the original 4–32-bit precision
left-aligned. The 1–8 default channel positions follow RFC 9639 and use the same
speaker names as the WAVE/AAC modules. Explicit masks matching the default layout
are accepted; other masks are unsupported.

Frame headers, reserved/forbidden syntax, CRC8/CRC16, zero padding, reconstructed
sample ranges and continuous frame/sample numbering are checked before output.
Frame timestamps start at sample zero. Fixed/variable blocking and final short
blocks are supported; when drained, known STREAMINFO sample totals must match.
Changing rate/channel count/precision mid-stream is unsupported. STREAMINFO MD5
verification, seeking and encoding remain open.

`FlacDecoderFactory(maxFrameBytes)` bounds individual encoded frames (default
4 MiB); nonzero STREAMINFO frame limits also apply. Input packets retain their original leases. Encoded storage, prediction
workspace and returned PCM use the supplied media allocator. Calls must not
overlap. Failure/cancellation requires reset or disposal. Reset starts again at
frame zero; returned frames retain independent leases.

```bash
dotnet run --project tools/Mediana.Cli -c Release --no-build -- audio decode tests/Mediana.Tests/Data/Flac/stereo-covers.flac artifacts/flac.s32le --stream 0
dotnet run --project tools/Mediana.Cli -c Release --no-build -- audio waveform tests/Mediana.Tests/Data/Flac/mono-speech.flac artifacts/flac-waveform.json --stream 0 --sample-rate 400 --samples-per-window 37 --gains 1
```

The complete PCM from musical and recorded-speech 16-bit inputs, stereo and 5.1
24-bit files, and a 32-bit mid-side vector matches independent decoded bytes and
sample counts. One-byte encoded packets are checked. All 400 Hz PCM16 waveform
RMS/peak windows and timestamps for music/speech match their original WAVE sources
exactly. The shared decoder checks use a 512 KiB media-buffer budget and verify
retained frame ownership and full release. See the
[FLAC references](tests/Mediana.Tests/Data/Flac/README.md) for provenance and commands.

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

Movie-level `meta` full boxes expose `Metadata` and `CoverArt`. The reader supports
iTunes `mdir` item lists in movie user data and indexed `mdta` keys in a movie
metadata atom. Values retain their namespace, name, declared data type and raw locale;
repeated/localized values remain separate. `Text` decodes UTF-8/UTF-16BE strictly,
and is null for other types. `ReadMetadataValue(index)` returns the original bytes
for binary fields such as track/disc declarations and unsupported data types.
Freeform iTunes keys retain their explicit namespace and name. Text bytes and value
counts have additional configured limits within the existing movie/box budgets.

```csharp
Mp4CoverArtInfo info = reader.CoverArt[0];
using MediaBuffer cover = reader.ReadCoverArt(0);
await output.WriteAsync(cover.Memory);
```

Cover declarations include iTunes `covr` and indexed `com.apple.quicktime.artwork`.
JPEG/PNG/BMP declarations expose a content type; unsupported declarations retain a
null content type and accessible original bytes. The reader does not decode or
validate the image codec. Cover bytes are separate from timed audio/video tracks.
An extracted lease survives reader disposal and retains the full backing movie
metadata allocation. Reading metadata/cover values does not move packet or source
cursors. The `cover mp4` command takes input, a new image output path and `--index`;
it preserves the original encoding and bytes. Existing files are not replaced.
The caller chooses an output suffix matching the declared image type.

Movie metadata names are not normalized to application fields. Track/media-level
metadata, locale-list resolution, legacy QuickTime user-data text, headerless QuickTime
metadata atoms and other handlers/indexed-key namespaces remain unsupported. Malformed structures, invalid text
and unsupported metadata handlers fail explicitly. See the
[metadata fixtures](tests/Mediana.Tests/Data/Metadata/README.md) for exact byte,
Unicode and packet comparisons in managed and Linux Native AOT checks.

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
MP3 `mp4a` entries with ES object type `0x69`/`0x6B` obtain their configuration
from a bounded four-byte read of the first Layer III frame, without moving packet
cursors. Each sample contains one complete MP3 frame. Container declarations
remain available separately from the parsed frame configuration. Empty MP3 tracks,
Layer I/II and free-format audio are unsupported. The existing MP3 decoder can
decode these streams through the public audio session; edit-list trimming remains
an explicit application operation.
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
rates. The reader does not apply rotation/display transforms. A sync-sample
flag alone does not establish HLS segment independence.

`Mp4Reader` implements the optional `IIndexedContainerReader` contract. Packet
counts, timestamp-based sync candidates and packet reads by index preserve the
reader's sequential cursors. Candidate lookup uses the sample tables, including
composition offsets and the selected edit-list mapping, without reading encoded
media. A candidate is a container declaration; the decoder must validate that it
can restart there. Sample lookup follows the
[QuickTime seeking procedure](https://developer.apple.com/documentation/quicktime-file-format/seeking_with_a_quicktime_file).

Mp4Reader.DisplayMatrix and Mp4TrackInfo.DisplayMatrix preserve the movie and track
header matrices. A video stream's EncodedStreamInfo.DisplayMatrix composes the track
transform followed by the movie transform. VideoSession.Stream exposes this selected
stream metadata; decoding leaves the raster unchanged. Matrix interpretation follows
the [QuickTime matrix definition](https://developer.apple.com/documentation/quicktime-file-format/matrices).

Fragmented files, encrypted/external data, compressed QuickTime headers, multiple
sample descriptions and other sample/handler types fail explicitly. The current
sample entries are avc1/avc3 and version-zero mp4a with AAC object type 2, 5 or 29,
or MPEG-1/2 Layer III under ES object type indication 0x6B/0x69.
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
MediaTimestamp target = new(1, new TimeBase(1, 1));
await session.SeekAsync(target);
using VideoFrame? frame = await session.ReadFrameAtOrAfterAsync(target);
```

The namespaces are Mediana, Mediana.Codecs.H264, Mediana.Containers.Mp4,
Mediana.Core.Timing, Mediana.Core.Video and Mediana.IO. The example selects the first
frame at or after one second in the original media timeline. It restarts at a
validated IDR access point and decodes forward from there, including reordered
pictures. Null means the stream has ended.

`H264RandomAccessInspector` examines AVC packets without reconstructing pictures.
`IsIdr(packet)` ignores container key-frame flags and validates access-unit boundaries
and all supported IDR slice headers, including their parameter references and picture
identity. It requires a progressive 8-bit 4:2:0 IDR picture whose first slice starts at
macroblock zero and whose remaining slices proceed in increasing order. It does not
validate residual data or prove complete macroblock coverage. Full non-IDR slice
headers, including B pictures, are not parsed by this
inspection operation. Packet inspection uses the original configuration and any
parameter sets carried within that packet, without retaining state between calls.

The decoder accepts AVC length-prefixed packets with one complete
picture and an initial avcC configuration. Supported pictures are progressive
8-bit YUV 4:2:0 CAVLC or CABAC I/P/B with default scaling, including 4x4/8x8
prediction and transforms.
Decoding begins with an IDR; sequence changes through in-band SPS/PPS also
require an IDR. Each output carries
its own format, color information, sample aspect ratio, presentation timestamp and
duration. Cropping is applied to YUV storage; rotation, edit lists and display aspect
transforms are not applied. Missing packet timestamps remain absent; unspecified
sample aspect ratio and CICP color codes remain unspecified.

Picture order types 0, 1 and 2, bounded delayed output, IDR/MMCO resets, input
completion, cancellation and buffer ownership have executable checks. SEI message
boundaries are validated; payload semantics are not interpreted. Slices have independent
prediction and entropy state; deblocking follows complete picture reconstruction,
honouring each slice's controls and reference identities. Missing or overlapping
macroblock ranges and conflicting picture identities or reference marking fail explicitly.
Arbitrary slice order, slice groups, redundant slices, interlacing, custom scaling,
higher bit depths and inferred references for frame-number gaps are unsupported.
H264DecoderOptions bounds picture
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

`Yuv420AreaResizer` filters progressive, even-sized YUV420P8 video directly in its
three planes. `Create` selects exact smaller dimensions; `CreateFit` fits storage
into an even-sized box without enlargement. It adjusts known SAR to preserve exact
display aspect, and preserves unknown SAR, color, PTS and duration. Explicit chroma
location is required. All six positions follow H.264 Figure E-1; unspecified or
invalid positions fail rather than receiving an assumed interpretation.

```csharp
using Yuv420AreaResizer resizer = Yuv420AreaResizer.CreateFit(frame.Format, 640, 360, allocator);
using VideoFrame smaller = resizer.Apply(frame, cancellationToken);
```

Reference Mediana.Processing. Reuse one plan for frames with the same dimensions,
pixel format, SAR and color; calls and disposal must not overlap. The plan stores
allocator-budgeted integer sample ranges and creates one owned output buffer per
frame. Input row strides may include padding. Output survives plan/input disposal;
cancellation and allocation failure leave the plan reusable. The area filter
integrates stored sample values with repeated edges and one final integer rounding.
It does not perform linear-light filtering, orientation, color conversion or
upsampling. Six chroma-position cases and 30 complete decoded pictures match
[exact rational references](tests/Mediana.Tests/Data/Resize/README.md).

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

## Encoding H.264

`H264Encoder` accepts progressive, even-sized 8-bit planar YUV 4:2:0 frames.
It writes Constrained Baseline level 4.1 AVC, using intra4x4 DC prediction,
integer transforms, scalar quantization, chroma DC transforms and CAVLC residuals.
Each input becomes one independently decodable IDR packet; deblocking is disabled.
The fixed quantizer is an H.264 QP from 0 to 51, with 26 as the default.

```csharp
using H264Encoder encoder = H264Encoder.Create(frame.Format, timeBase, allocator,
    new H264EncoderOptions { Quantizer = 26 });
if (encoder.TrySend(frame, cancellationToken) != CodecInputStatus.Accepted)
{
    throw new InvalidOperationException("Receive pending encoder output first.");
}
using MediaPacket packet = encoder.Receive(cancellationToken).Value
    ?? throw new InvalidOperationException("The encoder produced no packet.");
```

Reference Mediana.Codecs.H264 and use the Mediana.Codecs, Mediana.Core,
Mediana.Core.Memory and Mediana.Core.Timing namespaces. `encoder.Stream` supplies
the AVC configuration and stream clock for `MpegTsWriter`; packets use four-byte
NAL lengths, preserve exact input PTS/durations and have DTS equal to PTS.
Inputs need positive, exact durations and nonoverlapping timestamps in that clock.
Dimensions, pixel format, SAR and color metadata must remain constant. Range and
chroma location require explicit values. Padding repeats edge samples and crop
fields retain visible dimensions. Frame dimensions/rate, access-unit size and
per-frame bitrate are bounded against level 4.1 limits; this is not rate control.

The encoder borrows input storage, retains at most one owned output packet and
uses the caller's allocator for picture reconstruction and bounded encoded storage.
Receive pending output before sending another frame. `CompleteInput` drains that
packet; `Reset` discards pending state and permits a new sequence. Cancellation or
failure requires reset or disposal. Returned packets survive both operations.

The CLI can decode supported MP4/MOV video and encode it to a video-only TS file:

```sh
mediana -i input.mp4 output.ts --video-codec H264 --video-only --qp 26
```

Audio omission must be explicit. Without sizing options the command preserves
original dimensions and SAR. Optional `--width` and `--height` bound encoded storage
dimensions; either may be given alone. Bounds are rounded down to even values and
never enlarge the source. YUV area filtering precedes encoding, and SAR compensates
even rounding to preserve display aspect. Both bounds must be at least two.
The command requires an identity display transform and reports progress during longer
operations. Output is published only after successful completion. Temporal
prediction, motion search, CABAC encoding and bitrate/quality control remain open.
Experimental [AAC encoding](docs/aac-encoding.md) supports audio-only TS and
combined encoding in HLS. HLS video variants use the same encoder and area
filter through the [HLS encoding path](docs/hls-encoding.md). This initial
all-IDR mode does not promise bitrate efficiency comparable to inter-frame encoding.

All visible samples in 48 encoded pictures match independent decoder references
in managed and Native AOT tests. See the [reference manifest and commands](tests/Mediana.Tests/Data/Mp4/ENCODING.md).

## Writing an MPEG-TS program

Reference the Mediana.Containers.MpegTs source project and configure the output
with one to sixteen AVC/AAC/MP3 streams. The module depends on container/core
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

For a program with exactly one AVC track, `BeginSegmentAsync(nextOutput)` flushes
the previous borrowed stream and continues in a different writable stream.
The next packet must be that track's IDR access unit. PAT/PMT begin the new file;
all PID counters, PCR state and timestamp offsets continue across the boundary.
No stream is closed. A pending empty segment cannot be completed or skipped.
Callers select boundaries and keep audio packets in global decode order.

`CaptureSegmentState()` records the current configuration, limits, clocks and
counters without I/O. After disposing the preceding writer, use
`MpegTsWriter.ResumeSegment(output, state, allocator)` to write the next segment
from that state. Its first packet must be the next AVC IDR. Snapshots are immutable,
retain no I/O resources or media buffers and can be reused for requests in any
order. Given the same packets, resumed segments match continuous output byte for
byte. A snapshot does not select packet ranges or prove decoder independence.

The writer uses the caller's timeline. The MP4 reader defaults to original media
timestamps; select LinearMovie for the supported edit-list translation before
stream copy. Audio priming/discard metadata and container display matrices are not
represented in TS. This operation does not yet define HLS segment boundaries,
seek or playlists.

The sample copies every AVC/AAC access unit from a supported MP4/MOV into a new
TS file, applying LinearMovie timestamps and the writer's one-second offset:

```sh
dotnet run --project tools/Mediana.Cli -c Release -- \
  remux input.mp4 output.ts
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
dotnet run --project tools/Mediana.Cli -c Release -- \
  hls plan input.mp4 --video-stream 0 --segment-ms 2250
```

The 7.2-second closed-GOP AVC/AAC fixture produces video windows starting at
0, 3 and 6 seconds with durations 3, 3 and 1.2 seconds. Managed and Native AOT
checks verify exact rational times, sample windows and reverse boundary reads.
Transport tests also split the fixture into three files with one writer, retaining
all 36 AVC and 339 AAC packets. Every file and their concatenation pass packet,
timestamp, table, clock and continuity checks in managed execution and Native AOT.
The video plan does not include audio or transport state. The session below binds
them to one source. Neither API declares INDEPENDENT-SEGMENTS. Segment requirements follow
[RFC 8216](https://www.rfc-editor.org/rfc/rfc8216.html).

## Serving MP4 as HLS without transcoding

The experimental Mediana.Streaming.Hls source module provides `Mp4HlsSession` for
one supported AVC video track and an optional AAC-LC or MPEG-1/2 Layer III track. Open it on an immutable
IRandomAccessByteSource, selecting stream indices explicitly:

```csharp
await using Mp4HlsSession session = await Mp4HlsSession.OpenAsync(source, allocator,
    videoStreamIndex: 0, audioStreamIndex: 1, cancellationToken: cancellationToken);
string playlist = session.CreateVodPlaylist(number => $"segment-{number}.ts");
await session.WriteSegmentAsync(2, output, cancellationToken);
```

The session owns its MP4 reader and borrows the source. Opening inspects candidate
IDR headers and scans the selected compressed packets once, building audio sample
ranges and immutable transport snapshots. Media payloads and encoded segments are
not retained. MaximumSegments bounds index entries; MP4 and transport options bound
metadata, configuration and packet sizes. Unselected tracks are not copied.

Requests may arrive in any order and repeat. Each reads only its indexed sample
ranges, preserving continuous PID counters, clocks and PTS/DTS across the sequence.
Video wins equal decode-time ties. Audio packets before the next video's decode
start belong to the preceding segment. With B pictures this can place audio before
the first video presentation timestamp in a file. AAC requires PTS equal to DTS,
positive sample durations and increasing decode timestamps.

CreateVodPlaylist returns LF-delimited text for UTF-8 encoding, version 3, a zero
media sequence, VOD and ENDLIST. EXTINF follows actual video boundaries on the
90 kHz transport clock, including any declared audio tail in the last segment;
TARGETDURATION is the ceiling of the largest interval. Segment URIs are supplied
by the application and must be bounded, well-formed URI lines. No variant metadata
or independent-audio claim is inferred.

One session permits one I/O operation at a time. The caller keeps the source
unchanged and alive, manages concurrent sessions and owns every destination.
WriteSegmentAsync flushes without closing it. On failure or cancellation discard
the partial destination; the same segment can be retried using the immutable index.
Disposal releases reader metadata. Segment metadata and playlist formatting remain
available afterward.

This copies compressed streams. It does not encode, resize, normalize display
orientation, recover damaged pictures or trim MP4 edit windows. AAC priming and
padding are preserved; MP4 discard metadata and a shortened last AAC sample duration
are not represented in ADTS. Audio-independent decoding, additional codecs and
multiple renditions remain outside this path. Existing AVC inspection and MPEG-TS
configuration limits apply.

MP3 frames are copied byte-for-byte with MPEG-1/2 transport stream types
`0x03`/`0x04`. Each packet must contain one complete frame with stable sample rate,
version and channel count; VBR and frame padding are preserved. MPEG-2.5 transport
is unsupported. Audio reservoir dependencies and synthesis history remain intact,
so starting a segment alone can lose leading audio; the playlist does not declare
`EXT-X-INDEPENDENT-SEGMENTS` or add preroll. This path supplies continuous stream
copy and does not promise sample-exact independent audio starts.

[MP3-in-MP4 references](tests/Mediana.Tests/Data/Mp4/MP3.md) cover independent
packet hashes/timestamps, both MPEG versions, TS program tables and repeatable
HLS segments in managed and Linux Native AOT execution.

Managed and Native AOT checks cover the real 36-picture/339-AAC fixture, exact
sample windows, all compressed packets, playlists, transport continuity and repeated
requests in order 2, 0, 1, 2. Error tests cover short range reads, cancellation,
partial output, flush failures, invalid requests and source ownership.

The sample exports the indexed session into a new directory:

```sh
dotnet run --project tools/Mediana.Cli -c Release -- \
  hls create input.mp4 output-hls --video-stream 0 --audio-stream 1 --segment-ms 6000
```

Omit `--audio-stream` for video-only output. The directory's parent
must exist. All files are prepared in a temporary sibling directory, then moved
into place together; an existing destination is preserved and failures remove the
temporary output. The same command works with the Native AOT sample executable.
An independent decoder reads the resulting playlist with all 36 video and 339 AAC
frame hashes matching the source. Each TS file also decodes its 15/15/6 video frames
independently. Managed and Native AOT exports produce identical playlist and segment
bytes; existing-output and failed-export checks preserve destination ownership.

## Encoding HLS video variants

```sh
mediana hls encode input.mp4 output-hls --video-only --segment-ms 6000 --qp 26 --widths 1280 640
mediana hls encode input.mp4 output-av --audio-stream 1 --segment-ms 6000 --qp 26 --widths 1280 640
mediana hls encode input.mp4 output-aac --audio-stream 1 --audio-codec Aac --audio-bit-rate 128000 --widths 1280 640
```

The command decodes supported MP4/MOV AVC video once and produces one smaller
YUV420P8/H.264 sequence per width. Width bounds round down to even storage sizes
without enlargement; bounds that produce identical dimensions fail. `--widths`
uses space-separated integers and must be the final option. Audio omission is
explicit through `--video-only`; alternatively, `--audio-stream` copies one selected
AAC-LC or MPEG-1/2 Layer III track to every variant. Adding `--audio-codec Aac`
decodes that track and uses the experimental AAC encoder instead. Its optional
`--audio-bit-rate` ceiling defaults to 128000 raw bits/second for every variant;
rate and channel order are preserved. The command requires an identity display
transform.

Each variant contains IDR-aligned TS segments and a VOD
media playlist. The master playlist records actual peak/average transport bitrates
and the video/audio codec identifiers. Only video-only output declares independent
segments. Copy mode preserves original audio bytes and linear-movie timing.
AAC encoding applies the exact input MP4 sample edit, preserves its movie origin
and writes one shared encoded audio sequence with its own priming and final tail.
Segment timelines must match across variants. Output
is prepared in one temporary directory and published together after completion;
errors or cancellation remove it. The library also exposes `AvcHlsWriter` for
streaming encoded packets without a source-container dependency.

The current mode uses fixed-QP all-IDR video. Inter-frame compression, target
bitrate control, perceptual AAC quality and per-variant audio rates remain open.
See [HLS encoding contracts and checks](docs/hls-encoding.md).

## Connecting modules

Applications supply container and decoder factories; none are registered implicitly.
Factories declare stable implementation identifiers, and decoder factories also
declare the codec identifier and validate a specific stream configuration.
Factories must have immutable descriptors and support independent session creation.
Decoder factories may implement `IRandomAccessDecoderFactory` to provide restart
inspection; indexed containers implement `IIndexedContainerReader` independently.

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

`VideoSession.CanSeek` reports whether the container supplies indexed packets and
the decoder supplies restart inspection. `SeekAsync(target)` restarts at a
codec-validated candidate at or before the target, or at the first packet when
the target precedes it. Follow it with `ReadFrameAtOrAfterAsync(target)` to discard
preroll. Seeking can move backward and restart after EOF; retained frames remain
valid. The MP4/MOV and H.264 combination supports this operation. A container
sync flag alone cannot authorize decoder restart. Unsupported seeking fails
explicitly. Cancellation or seek failure makes the session terminal; dispose it.
Packet and frame budgets remain cumulative across seeks.

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

Random access to bytes alone does not provide video seeking. The selected
container and decoder must also support indexing and restart inspection.

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
  planar/interleaved layouts and exact sample-count duration. WAVE PCM decoding,
  explicit channel matrices, storage conversion and streaming sinc resampling are
  available, including limited AAC-LC decoding. Broader compressed audio,
  inferred delay/padding correction and playback remain open. Explicit sample
  trimming and selection from supported MP4 edit lists are available.
- A codec can accept several packets before output, or produce several frames
  from one packet. Completing input drains delayed output; resetting discards it.
  Input is borrowed for a call, retained input needs a separate lease, and output
  belongs to its caller.
- Execution is sequential and demand-driven. It does not prefetch unbounded queues
  or launch worker pools. One codec instance is not called concurrently.
- Cancellation is cooperative. Cancellation or decode failure makes the session
  terminal and releases decoder state; dispose the session to release reader state.
- `MaxPacketReads` counts all container reads, including seek candidate inspection,
  skipped streams and EOF
  checks. `MaxFrameReads` counts session reads, including EOF checks and frames
  consumed while selecting a timestamp. Both are optional positive work budgets;
  null, the default, permits complete media of any duration. Memory and format
  validation limits remain active. Work budgets are not timeouts.

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
the two original MP4/MOV fixtures, an MP4-to-PNG preview, WAVE/AAC/FLAC PCM export and resampling.
The separate native tests
also verify internal and public decoding of 85 CAVLC reference pictures and
six CABAC intra pictures, 96 additional CABAC I/P pictures and 98 CABAC I/P/B pictures,
SDR conversion, decoded PNG pixels, complete WAVE/AAC/FLAC references, FLAC waveform data and audio resampling. Each future
format also requires an executable AOT integration test.

Current limitations include broader compressed codec support, other container formats, audio seeking,
more general movie timeline mapping, arbitrary changes within a coded sequence, broader display transforms,
broader color conversion, broader compressed audio, artwork in other containers, Cotton provider integration, inter-frame video encoding, perceptual AAC encoding, per-variant audio rates, other muxers, scheduling,
pooling, SIMD optimization and playback. The contracts are experimental and may
change before 1.0.

Production format scope and performance targets remain open. Measurements and
independent comparisons cover selected inputs and do not establish general
performance or quality parity across media formats.

## Packages

```sh
dotnet pack Mediana.slnx -c Release --no-build --no-restore -p:Version="$PACKAGE_VERSION" -o artifacts/packages
```

The library projects depend at runtime only on .NET and other Mediana projects.
Build analyzers and test tools are development dependencies.
Packages contain an experimental foundation and the limited MP4/MOV reader described
above. The experimental H.264, PNG, WAVE, PCM and AAC source modules are not packaged. APIs may change before 1.0.

| Package | Responsibility |
| --- | --- |
| Mediana | Public facade and module registration |
| Mediana.Core | Time, packets, frames and buffer ownership |
| Mediana.IO | Byte sources and probe replay |
| Mediana.Containers | Container contracts |
| Mediana.Containers.Mp4 | Optional MP4/MOV metadata and packet reader |
| Mediana.Codecs | Codec contracts |
| Mediana.Processing | SDR-to-sRGB conversion, RGBA32 resizing and YUV420 area downscaling |
| Mediana.Execution | Sequential video decoding pipeline |

Installing Mediana includes its required modules through NuGet dependencies.
Mediana.Processing and Mediana.Containers.Mp4 are optional separate packages.
All packages share one version.

### Publishing

GitVersion computes the version from Git history and release tags using
[GitVersion.yml](GitVersion.yml). All library packages use the same computed version
for both compilation and packing. Ordinary releases advance the patch version;
minor and major changes require an explicit versioning decision.

Pushes to main and pull requests run build, formatting, tests and Native AOT checks
on Linux x64, Windows x64 and macOS ARM64. Successful builds upload standalone
executables, optional archives and SHA-256 checksums as workflow artifacts.

After all checks pass, pushes to main and manual workflow runs from main publish
the same validated packages to NuGet.org and GitHub Packages, and symbols to
NuGet.org. Pull requests only validate. The GitHub release is created after both
registries succeed. Its downloads contain the tested native executables, archives
and checksums; NuGet packages are available from the registries.

The release action uses the push's final commit message as its description.
The message should summarize the milestone's changes and relevant validation.
No separate release-description generator or manually maintained changelog is
required.

Authentication uses [NuGet Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing).
Set the NUGET_USER repository variable to the NuGet profile username. Configure
the matching NuGet policy for repository bvdcode/Mediana, workflow file
publish-release.yml, environment nuget, and package patterns Mediana and Mediana.*
with permission to push new packages and versions.
GitHub Packages uses the workflow's `GITHUB_TOKEN` with `packages: write` and the
feed `https://nuget.pkg.github.com/bvdcode/index.json`. Each package is linked to
this repository through its `RepositoryUrl`. GitHub package visibility must be
checked after the first publication; newly created packages default to private.

Re-running a partially completed publication skips existing package versions and
GitHub releases. Package IDs become assigned after successful publication.

## License

[MIT](LICENSE).

Apply source license headers with `dotnet run --file UpdateLicense.cs -- .`.
Validate them without editing files with
`dotnet run --file UpdateLicense.cs -- . --check`; the build workflow runs this check.
The updater skips generated files and build-output directories.


## PDF previews

```sh
dotnet run --project tools/Mediana.Cli -c Release -- pdf render report.pdf page.png --page 1 --width 1200
dotnet run --project tools/Mediana.Cli -c Release -- pdf image report.pdf image.png --page 1 --index 0
```

Pages start at one; decoded image indices start at zero. Output files must be new.
Page previews use 96 DPI, reduce proportionally to the requested maximum width and
apply a white background. Image extraction returns RGBA pixels, including masks,
without the image's placement on the page. The supported PDF subset, limits,
font behavior and known rendering gaps are described in [docs/pdf.md](docs/pdf.md).

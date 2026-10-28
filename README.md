# Mediana

Mediana is an MIT-licensed multimedia library under development for .NET 10.
Its media implementation is written in C#, with no FFmpeg, external media processes,
native media libraries, or third-party runtime packages.

The current implementation is experimental. It reads metadata and compressed packets from
non-fragmented MP4/MOV files containing AVC/AAC tracks.
**Public compressed video/audio decoding and encoding are not available yet.**
The sample can inspect real MP4/MOV inputs and run a separate generated RGBA example.

## Components

| Project | Current responsibility |
| --- | --- |
| `Mediana.Core` | Rational time, stream metadata, packets, video/PCM frames, buffer ownership and allocation budgets |
| `Mediana.IO` | Sequential and random access byte sources, stream ownership and probe replay |
| `Mediana.Containers` | Reader/factory contracts and a writer finalization contract |
| `Mediana.Containers.Mp4` | Non-fragmented MP4/MOV metadata and AVC/AAC packet reading |
| `Mediana.Codecs` | Separate input/output codec protocol and video decoder factories |
| `Mediana.Codecs.H264` | Experimental AVC parsing and internal CAVLC I/P picture reconstruction; no public decoder or package yet |
| `Mediana.Processing` | Nearest-neighbor resizing of RGBA32 storage |
| `Mediana.Execution` | Pull-driven video decoding, delayed output, bounded container reads and cancellation |
| `Mediana` | Explicit registration, container detection and forward-only video sessions |
| `Mediana.Sample` | Real MP4/MOV packet inspection, generated-frame example and Native AOT checks |

Core does not reference the other components. Containers do not reference codecs,
and codecs do not perform I/O. The public facade composes these contracts.

The H.264 work currently reconstructs single-slice progressive 8-bit 4:2:0 I/P
pictures with CAVLC and default scaling, including motion compensation, reference
picture management, intra prediction, inverse transforms, PCM blocks and deblocking.
Two complete Baseline/Main CAVLC sequences and one cropped IDR picture match the
independent filtered YUV references sample for sample (nineteen pictures total).
The Main sequence includes explicit prediction weights and reference-list changes. CABAC, B pictures and public
decoder integration remain open, so file preview extraction is not available yet.

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
```

The self-test decodes a generated 2×2 RGBA packet through the codec protocol, resizes
it to 1×1, checks the red output pixel and exact time conversion, then verifies
that all leased buffer bytes have been released. It does not open a video file.

Publish and execute it with Native AOT on Linux x64:

```sh
dotnet publish samples/Mediana.Sample -c Release -r linux-x64 -p:Version="$PACKAGE_VERSION" -o artifacts/native/linux-x64
./artifacts/native/linux-x64/Mediana.Sample --self-test
./artifacts/native/linux-x64/Mediana.Sample --probe-mp4 tests/Mediana.Tests/Data/Mp4/avc-aac.mp4
dotnet publish tests/Mediana.NativeTests -c Release -r linux-x64 -m:1 -p:IlcSingleThreaded=true -p:Version="$PACKAGE_VERSION" -o artifacts/native-tests/linux-x64
./artifacts/native-tests/linux-x64/Mediana.NativeTests
```

Native AOT requires the platform build toolchain. The resulting executable targets
one OS/architecture and still uses standard OS runtime facilities.
See the [.NET Native AOT prerequisites](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/).

Mediana.NativeTests executes internal H.264 I/P reconstruction and compares all
visible YUV samples in nineteen pictures with the checked-in references. It also
checks that picture allocations are released. Its fixture loading and comparison
code is shared with the unit tests. CI is configured to build and run both native
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
An H.264 video decoder must still be supplied for decoded video sessions.

The reader requires random access and a known source length. It scans box headers,
loads the bounded movie metadata and reads individual samples on demand. It does
not load the media data box while opening. It supports movie metadata at either
end of the file, sample/chunk mapping, decode time runs, signed/unsigned composition
offsets, sync samples and 32/64-bit chunk offsets. Packets are interleaved by decode
time; each track retains its sample order.

AVC streams expose the original avcC configuration and length-prefixed NAL packets.
AAC streams expose the AudioSpecificConfig and raw access units. Container-declared
audio channels and sample rate are labelled explicitly: codec configuration may
describe different values, which require codec-level interpretation.

Movie duration, track presentation duration, original media duration and edit lists
remain separate. Packet timestamps use the media time base before applying edits.
The reader does not yet map movie time to media time, apply rotation/display
transforms or seek. A sync-sample flag alone does not establish HLS segment independence.

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
copying; every lease must be disposed independently.

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
The native sample covers the generated-frame pipeline and reading all packets from
the two real MP4/MOV fixtures. Each future format also requires an executable
AOT integration test.

Current limitations include compressed codec implementations, other container formats, seeking,
movie-to-stream timeline mapping, dynamic format changes, display transforms,
color conversion, audio processing, encoding/muxing implementations, scheduling,
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
above. Compressed codecs are not implemented. APIs may change before 1.0.

| Package | Responsibility |
| --- | --- |
| Mediana | Public facade and module registration |
| Mediana.Core | Time, packets, frames and buffer ownership |
| Mediana.IO | Byte sources and probe replay |
| Mediana.Containers | Container contracts |
| Mediana.Containers.Mp4 | Optional MP4/MOV metadata and packet reader |
| Mediana.Codecs | Codec contracts |
| Mediana.Processing | RGBA32 resizing |
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

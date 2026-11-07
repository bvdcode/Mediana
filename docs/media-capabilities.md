# Indexed media processing capabilities

The library exposes selected audio/video operations through shared container,
codec, preparation and output contracts. Applications supply byte sources,
stream selection, encoder settings, destinations, cancellation and allocation
policy. File paths, console output and interactive overwrite prompts belong to
the client.

## Operations

| Capability | Public contract | Verified behavior |
| --- | --- | --- |
| Whole-file or selected audiovisual output | `MediaEngine.ProcessAsync`, `OpenProcessingAsync` | Independently copied or encoded tracks, explicit stream identities, bounded DTS merge and shared read budgets |
| Arbitrary cold HLS segment | `Mp4FragmentHlsSession`, `AvcHlsTranscodeSession` | Index-based copy or fresh interval encoding; no earlier segment state or whole-video pre-encoding; optional AAC copy/encoding and maximum video dimensions |
| Exact presentation selection | `MediaTimeRange`, `VideoPresentationReader`, indexed timing/presentation interfaces | Fractional/VFR clocks, reordered pictures, track offsets, explicit presentation edits, known tails and separate codec priming/padding |
| Shared video preparation | `VideoPreparationOptions`, `VideoPreparationSequence` | Display orientation, sample aspect ratio, native planar precision/sampling, SDR interpretation and fixed output geometry across compatible source changes |
| Expanded indexed input | Registered container and video decoder factories | VP8/VP9 in WebM/Matroska; MJPEG and MPEG-4 Part 2 in AVI; MPEG-4 Part 2 in MP4; Theora in Ogg |
| Complete-operation measurements | Comparison driver, processing benchmarks, independent references | CPU, elapsed time, peak RSS, cumulative managed allocation and decoded quality; before/after records include all frames and finalization |

Cold opening still reads the container index and required codec configuration.
Some containers also require bounded picture headers to establish display clocks;
packed AVI requires structural scanning to locate subframes. A segment request
reads its region plus the codec's required restart, reference or analysis context.
It does not promise arbitrary byte positioning without an index or codec preroll.

HLS uses fMP4 initialization and fragments for independent requests. The prepared
MPEG-TS playlist API has a different transport-counter contract and can scan media
in advance. Callers choose the operation explicitly.

## Input decoding and output encoding

| Input | Decoded representation | Integration checks |
| --- | --- | --- |
| VP8 | Native 8-bit YUV420; hidden reference pictures retained only for decoding | Eight independent streams, versions 0–3, reverse seeks, previews, complete AVC output and cold HLS |
| VP9 | Profiles 0–3; native 8/10/12-bit YUV and GBR sampling | Fourteen independent WebM streams, scaled references, superframes, show-existing pictures, reverse seeks, previews, complete AVC output and cold HLS |
| MPEG-4 Visual | Progressive 8-bit YUV420 I/P/B; half/quarter-pixel and one/four-vector motion | Native MPEG-4 and Xvid sources; exact AVI/MP4 clocks; packed AVI normalization; independent reconstruction; reverse seeks and whole/interval AVC output |
| MJPEG | Shared JPEG reconstruction and planar sampling | Interleaved AVI/PCM, OpenDML, legacy/indexless packet access and complete audiovisual AVC/AAC output |
| Theora | Theora 3.2 native YUV420/422/444 | Nine independent Ogg streams, fractional cadence, repeated pictures, Vorbis multiplexing, reverse seeks, previews and complete AVC output |

These decoder additions use the existing H.264 video and AAC audio output
factories. Encoding VP8, VP9, MPEG-4 Part 2 or Theora is separate work. Concrete
codec tools and unsupported syntax are documented in [VP8](vp8.md), [VP9](vp9.md),
[MPEG-4 Visual](mpeg4-visual.md), [MJPEG](mjpeg.md) and [Theora](theora.md).

A coded packet is distinct from a displayed picture: decode-only references
contribute no display interval. Exact planning and copy use the optional
`IIndexedVideoPresentationReader`, without introducing codec branches into the
application or HLS planner. Missing durations remain unknown, and explicit real
gaps remain gaps; strict complete-frame HLS rejects noncontiguous windows.

## Measured costs and remaining performance work

The following complete decode-and-hash observations use one CPU and one FFmpeg
decoding thread. They are separate recorded runs, with their own source revisions
and conditions; the reports contain the applicable comparison boundaries.

| Workload | Before CPU | After CPU | FFmpeg CPU | Report |
| --- | ---: | ---: | ---: | --- |
| VP8, 240 pictures at 720p | 5.303 s | 2.989 s | 1.603 s | [VP8](vp8.md#complete-decode-performance) |
| VP9, 240 pictures at 720p | 5.255 s | 3.473 s | 1.220 s | [VP9](vp9.md#complete-decode-performance) |
| Theora, 240 pictures at 720p | 2.885 s | 1.699 s | 1.157 s | [Theora](theora.md#complete-decode-performance) |
| MPEG-4, 1,440 pictures at 720p | 5.621 s | 5.118 s | 6.525 s | [MPEG-4](performance/results/2026-11-07-mpeg4/README.md) |

No additional full-frame copy is required by these optimizations. The reports
also retain noise and longer-duration cases; improvements vary substantially by
content. In particular, noisy VP9, Theora and MPEG-4 material retains a larger
reference CPU gap. Decoder throughput does not establish encoder parity.

[Common planar scaling](media-processing.md#presentation-intervals) uses caller-owned pooled
storage. [Cold HLS measurements](hls-on-demand.md#encoding-aac-audio) include a
30-minute indexed source and independent reverse/repeated audiovisual requests.
Their ten-minute complete AVC/AAC operation records essentially unchanged CPU
and allocations after bounded interval AAC analysis; no throughput gain is claimed.

[Processing contracts and examples](media-processing.md) describe selection,
ownership, exact clocks, cancellation and failure behavior.

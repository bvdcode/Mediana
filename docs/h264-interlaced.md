# H.264 interlaced decoding

The public decoder accepts picture-adaptive frame/field (PAFF) and
macroblock-adaptive frame/field (MBAFF) I/P/B sequences with CABAC or CAVLC,
8/10-bit samples and 4:2:0/4:2:2 chroma. Decoded frames carry actual sample
geometry, timestamps, field order and picture presentation hints. The same
`VideoSession` API handles sequential reading and seeking.

## Picture layout and references

The slice-header reader distinguishes frames, top fields and bottom fields.
Picture layout derives macroblock counts, pair addresses and picture-number
ranges from the sequence and picture structure. Separate fields use half the
coded frame height for pixel planes and block metadata. Adaptive frames use
one surface with per-pair frame/field geometry.

Picture ordering handles all three POC types, complementary field pairs,
frame-number wrap and reference resets. The reference buffer groups
complementary fields by a stable frame identity. Sliding-window occupancy
counts a pair as one frame; explicit marking can release either field, convert
a pair to long-term references or reset the buffer. Invalid marking preserves
the previous buffer and the caller's ownership.

Reference lists alternate field parity after sorting frame entries. H.264
7.4.3 limits active references to 16 for frames and 32 for fields. MBAFF slice
headers use the frame limit before expansion for field macroblocks. Field
views of material frames and separately stored field pairs retain reference
identity and picture order without copying the pixel surface.

Spatial motion prediction maps neighbours through their actual frame/field
geometry. Vertical vectors and reference indices use the scaling in H.264
8.4.1.3.2. Colocated direct prediction follows Table 8-8 for frames, separate
fields and adaptive frames. Temporal scaling and implicit B weights use the
selected field's POC; explicit weights index the original frame weight table.

## Reconstruction and filtering

Frame and field decoding share prediction, inverse transforms and interpolation
kernels. Pair mode selects the normative 4×4/8×8 coefficient scans and CABAC
significance contexts. CAVLC skip runs and CABAC skip/field flags preserve the
specified pair syntax order. CABAC history retains two macroblock rows for
ordinary pictures or two pair rows for MBAFF; storage scales with width.

Deblocking resolves physical neighbours using the pair grid. Mixed frame/field
edges use their own segment mapping, quantizers, boundary strengths and field
motion thresholds. A frame macroblock below field-coded neighbours filters
the two top-field boundaries separately. Slice-boundary suppression and
per-slice alpha/beta offsets remain applicable.

Uniform frame/frame and field/field edges reuse the existing filter kernels,
including 8-bit SIMD. Mixed edges use individual sample filtering. Threshold
calculation and reference/motion comparison are shared with ordinary decoding.
Filtering does not allocate a second pixel surface.

## Presentation and export

`VideoFrame.Presentation` preserves picture-timing SEI, including all nine
`pic_struct` values, repeated fields and repeated frames. In its absence the
decoder uses the inferred structure from H.264 D.2.3. Source scan indications
remain separate from presentation cadence. Packet PTS and duration remain
authoritative; repeated presentation does not duplicate pixel storage.

Strict decoding rejects a picture-timing display hint that contradicts the
coded picture structure or field order. Applications can explicitly enable
`H264DecoderOptions.IgnoreInconsistentPictureTiming` and pass a warning handler
to `H264DecoderFactory`. Recovery discards the inconsistent optional hint and
infers presentation from coded structure and picture-order counts. It preserves
packet timestamps, duration and reconstructed samples. Malformed SEI remains
an error. The warning is emitted once per decoder, including across seeks.

CLI preview, animation, video conversion and HLS commands enable this policy
and log its warning. An independently decoded contradictory MBAFF fixture
verifies identical raw samples, repeated seeks and retained-frame ownership.
Separate CLI processes each emit one warning; FFmpeg reads their PNG, TS and
HLS outputs, and libwebp confirms the animation's pixels and duration.

Complementary PAFF fields are assembled into full frames in presentation
order. A pair retains its leading timestamp and combines known field
durations. MBAFF frames with field presentation expose top-field-first or
bottom-field-first scan order. Explicit progressive source scan retains
progressive layout. Independent field chroma locations are preserved.

`VideoPreparationOptions.InterlacedField` selects spatial reconstruction from
the leading, top or bottom field. It produces one progressive image per input
frame, preserves its timing and restores full height. This is a spatial
single-field conversion, not motion-adaptive deinterlacing or frame-rate
doubling. Library callers opt in. CLI preview, transcode and encoded HLS select
the leading field and report the conversion as a warning. The preparation is
shared by the library export pipeline.

## Independent validation

The checked-in fixtures and their provenance are described in
[the MP4 fixture reference](../tests/Mediana.Tests/Data/Mp4/README.md).

- Fifteen PAFF and mixed frame/field sequences validate 252 public output
  pictures and reverse seeks, including every sample, timestamp, duration,
  scan mode, retained-frame lifetime and buffer release.
- MBAFF reconstruction cases cover PCM, 4×4/8×8 transforms, CAVLC/CABAC,
  predictive and bidirectional pictures, spatial/temporal direct prediction,
  forced fields and adaptive mixed pairs.
- Twenty filtered JM 19.1 sequences validate 240 pictures at 8/10-bit
  4:2:0/4:2:2. Eight additional sequences exercise multiple slices, nonzero
  chroma and deblocking offsets, and filtering across or within slice boundaries.
- Eight libx264 sequences validate cropped 94×92 images, both field orders,
  implicit B weights, three slices and repeated random access points. All
  twelve pictures per sequence match FFmpeg exactly.
- Public MBAFF checks read each frame sequentially and seek backwards to every
  frame, comparing samples, timestamps, durations and presentation. Shared
  cases also run under Native AOT.
- Synthetic checks cover all frame/field neighbour combinations, Table 8-8
  colocated mappings, explicit/implicit weights, both field parities and
  1,728 CABAC pair-flag combinations. Compact syntax history is compared with
  full-picture storage without allocations during traversal.

A ten-minute PAFF MP4 with AAC validates 7,200 video frames and 28.8 million
audio samples. Every video frame matches FFmpeg SHA256. One hundred joint A/V
seeks and one hundred requests between fields verify timestamp alignment and
selection of the following complete picture. Audio block RMS differs by at
most 0.000006 dB; this is an energy comparison, not a bit-exact waveform claim.
The allocator returns to zero live bytes. Its 96×96 resolution validates long
timelines and seeking, not HD throughput.

The same long-run validation also passes for an MBAFF 10-bit 4:2:2 source
with multiple slices and AAC: 7,201 pictures match independently decoded
hashes, 28.8 million audio samples cover 600 seconds, and both sets of 100
seeks pass. The copied source ends video one frame after the 600-second audio
boundary (600.083333 seconds); validation preserves those distinct timelines.
The maximum audio block RMS difference is 0.000001 dB and live buffer bytes
return to zero.

CLI checks cover a bottom-field-first PNG preview and High10 4:2:2 to H.264
conversion. A two-second MBAFF/AAC input produces two HLS renditions with five
aligned segments each. FFmpeg decodes both complete playlists without errors.
Warnings report the selected field and 10-to-8-bit reduction.

All 1,641 selected H.264 and CLI tests and the complete standard Linux Native AOT
test executable pass. The Native AOT output includes the profile/field media, decoded samples,
picture-order references and presentation timing tables.

## Current format boundaries

Reconstruction supports default scaling matrices and equal 8- or 10-bit luma
and chroma precision. Custom scaling matrices, transform bypass, separate
colour planes and SP/SI slices remain unsupported. These are independent of
PAFF/MBAFF picture structure. The H.264 encoder produces progressive 8-bit
4:2:0 output; interlaced input can use the preparation described above.

## MBAFF filter performance

The comparison measures the initial per-sample MBAFF filter against reuse of
uniform-edge filter kernels. Both are Native AOT builds on .NET 11.0.100.
Three runs of each binary alternate on one fixed physical core, with one
reported processor and pooled image buffers. Each process decodes and hashes
600 pictures from an independently encoded 150-picture 1280×720 source.
Every decoded SHA256 matches FFmpeg; all buffer leases return to zero.
These are two revisions of Mediana, not a comparison with FFmpeg throughput.

| Input | CPU before → after | Wall before → after | Peak RSS before → after | Allocated/run before → after |
| --- | ---: | ---: | ---: | ---: |
| 8-bit 4:2:0 | 60.380 → 31.160 s | 60.385 → 31.163 s | 36.63 → 36.66 MiB | 33.73 → 33.83 MiB |
| 10-bit 4:2:2 | 25.632 → 25.511 s | 25.635 → 25.513 s | 51.22 → 51.33 MiB | 48.01 → 48.04 MiB |

The 8-bit source uses 48.4% less CPU. The 10-bit source still uses scalar
filtering and shows no established improvement: CPU medians differ by -0.47%,
with overlapping run ranges. The speed gain uses existing SIMD instructions;
it is not attributed to a new .NET 11 intrinsic.

### Progressive regression comparison

The baseline is the preceding bidirectional MBAFF revision. Three alternating
Native AOT runs per revision decode and hash 600 pictures per source on the
same core. Every hash matches the independent reference. The shared threshold
calculation retains QP reuse outside each ordinary macroblock's edge loop.

| Input | CPU before → after | Wall before → after | Peak RSS before → after | Allocated/run before → after |
| --- | ---: | ---: | ---: | ---: |
| 720p CABAC | 7.590 → 7.641 s | 7.591 → 7.642 s | 33.89 → 33.91 MiB | 26.04 → 26.00 MiB |
| 10-bit 4:2:2 CABAC | 20.188 → 19.605 s | 20.190 → 19.607 s | 48.43 → 48.32 MiB | 40.33 → 40.31 MiB |
| 720p CAVLC | 7.190 → 7.252 s | 7.191 → 7.254 s | 33.73 → 33.79 MiB | 25.87 → 25.92 MiB |

CPU ranges overlap for all three inputs; these results do not establish a
progressive throughput change.

### Compatible presentation policy

Three alternating Native AOT runs compare the preceding strict-only revision
with the explicit recovery option enabled. Each run decodes and hashes 600
valid pictures on the same core; every sample hash matches the reference.

| Input | CPU before → after | Wall before → after | Peak RSS before → after | Allocated/run before → after |
| --- | ---: | ---: | ---: | ---: |
| 720p progressive | 7.553 → 7.460 s | 7.554 → 7.461 s | 33.82 → 33.81 MiB | 26.04 → 26.01 MiB |
| 720p MBAFF | 30.990 → 31.083 s | 30.996 → 31.086 s | 36.48 → 36.62 MiB | 33.81 → 33.82 MiB |

CPU ranges overlap for both inputs. No throughput change is established.

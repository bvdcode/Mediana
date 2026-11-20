# H.264 interlaced decoding

The public decoder still rejects interlaced sequences. Macroblock-adaptive
frame/field reconstruction, presentation metadata and deinterlacing remain open.

The shared slice-header reader distinguishes frame, top-field and bottom-field
pictures. Picture layout derives the macroblock count, MBAFF pair addresses and
picture-number range from the sequence and picture structure. It does not change
the sequence parameter set to represent a field as a progressive frame.

Picture ordering handles all three POC types, complementary field pairs, frame
number wrap and reference resets. Separate fields use only their own applicable
field count. A repeated reference frame number is accepted only for an eligible
adjacent field of opposite parity.

Field storage uses half the coded frame height for pixel planes and block
metadata. The reconstruction pipeline shares prediction and inverse transforms
with progressive decoding, selecting the normative 4×4/8×8 field scans and
field CABAC significance contexts. Deblocking uses field-specific horizontal
intra strengths and motion-vector thresholds measured in frame sample units.
The coefficient scan tables are shared with progressive transforms.

The reference buffer groups complementary fields by a stable frame identity,
independent of wrapping frame numbers. Sliding-window occupancy counts a pair
as one frame and releases both fields together. Explicit marking can release
one field, convert a pair to long-term reference, or reset the buffer. Invalid
marking preserves the prior buffer and the caller's input ownership.

Field reference lists sort frame entries before alternating field parity,
skipping absent fields. Short-term and long-term picture numbers use the current
field's parity. Inter-field 4:2:0 prediction adjusts the chroma motion vector
according to Table 8-10; 4:2:2 keeps the luma vector's vertical displacement.

Nine JM 19.1 sequences contain twenty-four separately coded fields each at
96×96 frame resolution. All 216 I/P/B fields match every independently decoded
FFmpeg luma/chroma sample, in managed execution and Native AOT. Coverage includes
CABAC, CAVLC, spatial/temporal direct B prediction, 8/10-bit precision and
4:2:0/4:2:2 chroma. The 10-bit sources exercise all four low-bit combinations.
The original two sequences also verify all 48 slice headers and POC values.
The output queue also assembles these fields into 108 complete frames in presentation
order, matching every reference sample in managed execution and Native AOT. Each
pair retains the leading field's timestamp and combines its two known durations
on an exact common clock. Unknown durations remain unknown.

A pending field retains its pixel lease independently of the reference buffer.
Pair assembly allocates one final interleaved surface and copies each visible row
once. It does not allocate full-size intermediate surfaces. Unpaired fields are
emitted with their actual field geometry; no missing field is synthesized. IDR
output-discard flags, reference resets, cancellation and failed output allocation
preserve the queue's ownership contract.
Additional tests cover all three POC types, parity-aware list modification,
frame-number wrap, long-term field assignment, buffer occupancy and transactional
ownership on invalid marking.

The full main test suite passes 4,529 tests. A separate Native AOT executable
also passes the twelve existing progressive 4:2:2/High10 sequences and their
reverse seeks, plus interpolation, prediction and deblocking vectors. These
results establish the tested stages, not complete interlaced playback or seeking.
Fixture generation and hashes are recorded in the
[MP4 test data documentation](../tests/Mediana.Tests/Data/Mp4/README.md).

## Progressive encoding regression

A Native AOT comparison retains the same six 150-frame 720p/1080p inputs and
encoder settings used by the [encoding matrix](h264-encoding-matrix.md).
Each binary runs on logical CPU 11 with one processing thread and priority
`nice -20`. Three runs per binary alternate in ABBAAB order; the animation
case additionally repeats five runs per binary after a large timing spread.
No other local build or reference encoding overlaps measured runs.

| Source | CPU before / after, s | Wall before / after, s | Peak RSS before / after, MiB | Allocations before / after, MiB |
| --- | ---: | ---: | ---: | ---: |
| camera-720 | 6.944 / 6.957 | 7.057 / 6.958 | 23.4 / 23.6 | 14.89 / 14.95 |
| noise-720 | 6.170 / 6.188 | 6.171 / 6.189 | 23.8 / 23.8 | 15.14 / 15.08 |
| text-720 | 2.044 / 2.063 | 2.045 / 2.063 | 23.2 / 23.2 | 14.64 / 14.64 |
| photo-pan-1080 | 9.922 / 10.015 | 9.927 / 10.019 | 37.2 / 37.2 | 37.83 / 37.83 |
| animation-1080 | 7.516 / 7.576 | 7.529 / 7.581 | 36.8 / 36.8 | 37.33 / 37.21 |
| cuts-1080 | 13.299 / 13.052 | 13.748 / 14.069 | 36.9 / 36.9 | 37.34 / 37.33 |

All 46 complete encoded transport streams are byte-identical to their previous
reference, preserving the decoded quality and file size. Retained allocator
bytes are zero after each run. Across the five stable original cases and the
animation repeat, median CPU changes range from −1.86% to +0.94%.
The first animation comparison had 7.634–10.994 seconds CPU before and
8.494–10.056 seconds after; its apparent 15.5% improvement is not treated
as an acceleration result. The additional repeat gives 7.516 versus 7.576
seconds (+0.81%). These measurements cover the effect on existing progressive
encoding, not complete interlaced decoding throughput.


## Progressive decoding regression

A separate Native AOT client compares the decoder before and after field
reference management and inter-field prediction. Three independent libx264
`veryfast` inputs contain 150 frames each, with QP 24, GOP 60, two B frames and
four reference frames. Each measured process decodes the complete file four
times (600 frames), including container reading and a SHA-256 of all visible
samples. All eighteen runs match independent FFmpeg decoding exactly and
release their allocator budget after every session.

Each variant runs three times in ABBAAB order, pinned to logical CPU 11 with
`nice -20` and one processing CPU. No local builds or source encoding overlap
timing. Values below are medians; allocations accumulate across all four passes.

| Source | CPU before / after, s | Wall before / after, s | Peak RSS before / after, MiB | Allocations before / after, MiB |
| --- | ---: | ---: | ---: | ---: |
| camera-720 | 7.654 / 7.707 | 7.660 / 7.713 | 59.64 / 59.46 | 1234.40 / 1234.46 |
| camera-422-10 | 20.120 / 20.078 | 20.132 / 20.088 | 110.89 / 111.04 | 2588.25 / 2588.28 |
| noise-1080 | 92.705 / 93.979 | 92.793 / 94.077 | 112.44 / 111.46 | 3046.89 / 3046.98 |

The camera-720 source uses 8-bit 4:2:0; camera-422-10 uses the same 720p camera
frames converted to 10-bit 4:2:2. Noise-1080 uses a 1920×1080 test pattern with
temporal uniform noise (`alls=18`, seed 1234). CPU median changes are +0.69%,
−0.21% and +1.37%, respectively. The noisy-input after runs span 92.681–104.338
seconds, versus 92.696–93.037 seconds before, so its timing is less stable.
This is a regression check, not an acceleration claim. Peak memory and total
allocations remain comparable; low retained memory does not imply low cumulative
allocations. No field output composition or RGB conversion is included here.

## Shared frame representation

`VideoFormat.ScanMode` distinguishes progressive storage, interleaved field rows
with top-first or bottom-first presentation, and a single stored field. Existing
constructors retain progressive behavior. Interleaved chroma plane heights are
calculated from each field separately, including odd and cropped dimensions.
`VideoFrame.GetField` returns an independent strided lease over the same allocation;
it preserves source timing without inventing a field timestamp or duration.

`VideoColorInfo.FieldChromaLocations` can preserve separate field locations.
The sRGB converter interpolates vertically within the selected field using the
sampling grid in H.264 Annex E, including high-precision planar samples. CICP
precedence and explicit color policies preserve known field locations. Color
conversion and horizontal RGBA scaling retain scan mode. Vertical scaling and
orientation changes that rearrange field rows require deinterlacing first;
progressive YUV preparation and H.264 encoding reject field storage rather than
silently relabel it. Deinterlacing remains open.

## Reference row layout

Luma/chroma interpolation and border extension distinguish the source row width
from its physical stride. The same 8-bit SIMD and 10-bit interpolation kernels
can sample alternating field rows without reading the other field or allocating
a full-frame intermediate. Packed references keep their existing sample layout.
Managed and Native AOT checks cover all luma/chroma fractions, both row parities,
borders and prediction block sizes. The 959 H.264 tests passed; all 16 motion
compensation tests also passed with hardware intrinsics disabled.

A complete-decode regression comparison used Native AOT on CPU 11, one reported
processor, priority -20 and the shared BCL pool. Each process decoded four full
150-frame sequences, hashed all output samples and checked that owned buffers
were released after each pass. Every output hash matched FFmpeg. The table shows
medians; variants ran sequentially in alternating order.

| Source | Runs per variant | CPU before / after, s | Wall before / after, s | Peak RSS before / after, MiB | Allocations before / after, MiB |
| --- | ---: | ---: | ---: | ---: | ---: |
| Camera, 720p, 8-bit 4:2:0, repeat | 5 | 7.416 / 7.448 | 7.416 / 7.449 | 33.46 / 33.34 | 25.92 / 25.93 |
| Camera, 720p, 10-bit 4:2:2 | 3 | 19.702 / 19.668 | 19.723 / 19.677 | 47.80 / 47.99 | 40.22 / 40.23 |

The initial three-pair 8-bit run measured 7.449 / 7.690 seconds CPU (+3.24%).
A five-pair repeat with reversed starting order measured +0.43%, with overlapping
ranges (7.399–7.451 before, 7.369–7.465 after). The initial difference did not
reproduce; this change does not establish an acceleration. The 10-bit difference
was -0.17%. These measurements check the existing progressive path, before mixed-reference
pixel prediction was added.

## Mixed reference marking

Logical reference identities distinguish the two fields of a material frame
without duplicating its pixel storage. Field lists alternate parity across frame
entries; list modifications retain the other field when both share one buffer.
Frame lists combine complete marked pairs and exclude unpaired fields, as required
by H.264 clauses 8.2.4.1–8.2.4.3. Frame-mode memory operations cannot target a lone
field. Reference marking is transactional, and removing one logical field keeps
its physical storage alive while the other remains referenced.

All 967 H.264 tests passed, including failed-marking rollback, frame-to-field
list transitions, frame list reconstruction and allocator release. These checks alone do not enable mixed interlaced streams through the public
decoder; macroblock-adaptive reconstruction and presentation remain open.


## Mixed reference pixels

Field views of material frames read alternating rows through the shared
interpolation kernels. Frame views of separately stored fields gather only the
prediction block and interpolation taps on the stack; they do not materialize a
full reference frame. Integer prediction copies the selected rows directly.

Two JM 19.1 mixed frame/field sequences exercise both CABAC and CAVLC, frame
number wrap and transitions in both directions. All 66 coded pictures and 48
assembled output frames match FFmpeg byte for byte, with exact presentation times
and durations in both managed execution and Native AOT. Separate kernel checks
compare views against material pictures for
8/10-bit 4:2:0/4:2:2, both field parities, every interpolation fraction and border
extension, also with hardware intrinsics disabled. The 973 H.264 tests pass, and
the standalone reference suite also passes with ordinary async compilation.
MBAFF and public interlaced decode/seek acceptance remain open.


## Mixed B-picture prediction

Co-located prediction selects the nearest reference field by picture order,
choosing the bottom field on a tie. Block coordinates, reference identity and
vertical temporal motion follow H.264 Table 8-8 and clauses 8.4.1.2.1–8.4.1.2.3.
Frame-to-field motion divides toward zero, including negative odd vectors;
field-to-frame motion doubles the vertical component. Spatial direct zero-motion
tests use the original co-located vector before temporal scaling.

Four independent JM 19.1 sequences cover CABAC/CAVLC and spatial/temporal direct
prediction with transitions between frame and field pictures. Each contains
15 B pictures among 33 coded pictures. All 132 coded pictures and 96 assembled
frames match FFmpeg exactly, including POC, output timestamps and durations,
in managed execution, Native AOT and ordinary async compilation. Five focused
checks also cover parity mapping, negative odd motion, field selection and ties.
The 982 H.264 tests pass. MBAFF and public interlaced decode/seek remain open.

## Mixed reference regression measurement

A Native AOT comparison measures the combined logical-reference, pixel-view and
co-located-motion changes against the preceding row-stride implementation. The
same two 150-frame 720p inputs are decoded four times per process, with pooled
buffers, one reported processor, CPU 11 affinity and priority -20. Three runs per
variant alternate in ABBAAB order, without overlapping local builds or encoding.
All twelve runs produce 600 frames, match the FFmpeg sample hashes and release
their allocator budget after each pass.

| Source | CPU before / after, s | Wall before / after, s | Peak RSS before / after, MiB | Allocations before / after, MiB |
| --- | ---: | ---: | ---: | ---: |
| Camera, 8-bit 4:2:0 | 8.161 / 7.949 | 8.169 / 7.959 | 33.35 / 33.50 | 25.83 / 25.94 |
| Camera, 10-bit 4:2:2 | 20.767 / 20.159 | 20.787 / 20.177 | 47.94 / 48.09 | 40.22 / 40.26 |

CPU medians changed by -2.60% and -2.92%, but the ranges overlap: 8.089–9.013
versus 7.422–8.167 seconds for 8-bit, and 20.204–22.054 versus 20.019–22.956
seconds for 10-bit. These results show no observed regression on the two inputs;
they do not establish an acceleration. Peak RSS increased by approximately
0.15 MiB, and cumulative allocations remain comparable. These measurements cover
progressive decoding; they do not establish mixed-interlace throughput.

## MBAFF macroblock geometry

A shared macroblock grid separates coded pair order, logical block coordinates
and physical pixel rows. The pair-mode map is part of the picture's allocator
budget. It uses one byte per MBAFF pair and no additional pixel surface. Field
pairs use alternating rows; frame pairs use contiguous rows. Neighbour lookup
applies picture bounds, slice boundaries and decoding order before accessing a
pair's mode.

Thirty-three checks cover the frame/field neighbour cases in H.264 Table 6-4.
Geometry checks exercise every combination of six pair modes for luma, 4:2:0
chroma and 4:2:2 chroma, covering every plane sample exactly once. Availability
and immutable pair-mode checks also pass.

Two independently encoded JM 19.1 CAVLC PCM pictures match every FFmpeg-decoded
sample in managed execution, Native AOT and ordinary async compilation. Field
flags, PCM sample placement and allocator release are verified. All 1,022 H.264
tests pass. Predictive MBAFF macroblocks, field-dependent entropy contexts and
mixed-edge deblocking remain open; the public interlace guard remains in place.

## MBAFF intra prediction

CAVLC intra reconstruction now maps prediction neighbours, coefficient counts,
4x4 and 8x8 transform scans, and reconstructed samples through the picture's
macroblock grid. Canonical block metadata is separate from physical frame or
field row placement. No additional full-frame surface is allocated.

Four JM 19.1 intra pictures cover 8-bit 4:2:0, High10 4:2:0 and 10-bit 4:2:2.
Three adaptive pictures contain both frame and field pairs; a fourth uses
field pairs throughout. All decoded samples match FFmpeg in managed execution,
Native AOT and ordinary async compilation. The 1,026 H.264 tests pass.
CABAC MBAFF, predictive macroblocks and mixed-edge deblocking remain open.

A full-decode regression check runs 600 progressive frames per process with the
same frozen Native AOT client, one reported CPU, a fixed core and shared pooled
buffers. Three runs of each revision alternate before/after order. All decoded
hashes match FFmpeg and each file releases its buffer leases. Median CPU time
is 7.437 → 7.397 seconds for 720p 8-bit 4:2:0 and 19.688 → 19.557 seconds for
720p 10-bit 4:2:2. These differences below 1% do not establish a speed gain.
Peak RSS medians are 33.48 → 33.45 MiB and 47.91 → 48.00 MiB; total managed
allocations are 25.9558 → 25.9569 MiB and 40.2508 → 40.2574 MiB, respectively.
These measurements cover two progressive sources, not MBAFF throughput.

## MBAFF CABAC intra reconstruction

CABAC intra reconstruction now derives pair-mode, macroblock type, coded-block
pattern, transform-size and coded-block contexts from the shared macroblock
grid. Residual significance contexts use each pair's frame or field mode.
Termination is read at pair boundaries; PCM restarts the arithmetic decoder.

Sixteen independent JM 19.1 MBAFF pictures now match FFmpeg sample-for-sample
in managed execution, Native AOT and ordinary async compilation. They cover
both entropy modes, 8/10-bit 4:2:0 and 4:2:2, adaptive mixed pairs, forced field
pairs, PCM and forced 8×8 transforms. All 1,036 H.264 tests pass. Predictive
MBAFF slices, mixed-edge deblocking and public interlaced playback remain open.

A repeated progressive 10-bit 4:2:2 check compares the CAVLC MBAFF geometry
revision with the CABAC MBAFF revision. Each Native AOT process decodes 600
frames; three runs per revision alternate on one fixed core. CPU medians are
19.701 and 19.726 seconds, with ranges 19.578–19.897 and 19.644–20.182 seconds.
Peak RSS medians are 48.07 and 48.23 MiB; total managed allocations are 40.282
and 40.275 MiB. All decoded hashes match FFmpeg. The overlapping ranges do not
establish a throughput change.

## Picture presentation

The decoder now preserves picture-timing SEI as `VideoFrame.Presentation`.
It reads HRD-dependent fields using the active slice's SPS, validates the
bounded payload and maps all nine display structures to frame or field
sequences. Without an explicit structure it uses the H.264 D.2.3 inferred
field order. Source scan indications remain separate from pixel layout;
unknown source scan is not guessed from display cadence.

Field pairing combines the display hints without creating repeated pixel
surfaces. Packet PTS and duration remain authoritative. Four independent
JM/libx264 sources match FFmpeg pixels and timing in forward reading and
reverse seeking, including repeated fields, frame doubling and HRD with
B-picture reorder. These checks pass in managed execution, Native AOT and
ordinary async compilation. The public interlace guard remains in place
until sample presentation and the remaining reconstruction paths are ready.

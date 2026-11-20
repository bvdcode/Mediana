# H.264 interlaced decoding

The public decoder still rejects interlaced sequences. Mixed frame/field reference
views, macroblock-adaptive frame/field reconstruction and deinterlacing remain open.

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
was -0.17%. These measurements check the existing progressive path; mixed
frame/field reference decoding remains unimplemented.

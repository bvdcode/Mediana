# H.264 interlaced decoding

The public decoder still rejects interlaced sequences. Combining complementary fields
for output, mixed frame/field reference views and macroblock-adaptive frame/field
reconstruction remain open.

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

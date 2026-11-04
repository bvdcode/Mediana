# H.264 macroblock boundary classification

Boundary-strength organizations were compared with the current decoder.
The calculations follow the mode, residual and physical-reference priority in
[H.264 section 8.7.2.1](https://www.itu.int/rec/T-REC-H.264-202606-I).
Mode and residual masks cover sixteen 4x4 cells, including shared 8x8 residuals.
The paired calculation shares current-block masks and uniform prediction across
vertical and horizontal boundaries within one synchronous operation.

Each CABAC operation decodes 180.125 seconds, 4,323 pictures and 5,976,115,200
YUV bytes. Both balanced orders use one decoder thread, one logical CPU,
ordinary Native AOT compilation and GC. Every output byte, rational timestamp,
duration, EOF and released media allocation is checked.

| Calculation | Order | Before CPU, s | After CPU, s | Change |
| --- | --- | ---: | ---: | ---: |
| Separate orientation masks | ABBA | 51.009 | 44.834 | -12.11% |
| Separate orientation masks | BAAB | 45.176 | 45.670 | +1.09% |
| Paired current-block masks | ABBA | 46.540 | 43.558 | -6.41% |
| Paired current-block masks | BAAB | 48.602 | 44.361 | -8.73% |
| Bidirectional-only paired masks | ABBA | 53.368 | 52.394 | -1.83% |

Separate masks fail the requirement of at least 5% CPU reduction in both orders.
The paired calculation passes that requirement. Its peak RSS medians are
71.54 to 80.93 MiB in ABBA and 71.52 to 71.74 MiB in BAAB.

The paired calculation also completes sixteen exact CAVLC/encoding controls.
CAVLC CPU changes by -0.28% and -0.06%. Encoding changes by -2.20% and +4.12%;
the reverse order exceeds the predefined 3% regression allowance. Therefore
the general paired replacement fails the complete acceptance criteria.

The bidirectional-only calculation retains the original scalar strength and
single-orientation implementation for I/P pictures. Four complete exact CABAC
operations give a 1.83% CPU reduction, below the same 5% minimum. Its peak RSS
medians are 71.74 and 70.95 MiB. This organization also fails CPU acceptance.

The mask calculations pass 524,288 independent sample-coordinate topology cases,
264,192 further topology cases and physical-reference, mutation and range
checks in managed, software and Native AOT execution. The paired calculation
adds 4,800 range/error cases, including partial writes and repeated-call
allocation checks. Each also passes 729 H.264 tests in both managed modes.

## Rolling physical-reference window

A separate calculation prepares physical reference identities and mode,
residual and deferred-error states in five rows of 4x4 cells. It reuses these
states across macroblocks during one synchronous deblocking pass and borrows
the existing motion arrays. At 1280 pixels wide, bidirectional metadata payload
occupies 27,200 bytes, excluding object headers. Byte states use the shared
media allocator; the typed reference arrays are separately bounded by width,
five rows and reference-list count.

The calculation passes 443,904 comparisons with the original scalar classifier
in managed, hardware-disabled and strict Native AOT execution. These cover
physical-reference aliases, both prediction lists, row transitions, residual
priority, extreme motion components and partial-error writes. Existing physical
reference, mutation and range checks also pass, along with 729 H.264 tests in
each managed mode.

Four complete CABAC operations in before/after/after/before order preserve
every output byte, timestamp, duration, EOF and released media allocation.

| Measure | Before median | After median |
| --- | ---: | ---: |
| CPU, s | 64.028 | 64.680 |
| Elapsed, s | 106.839 | 167.262 |
| Peak RSS, MiB | 70.63 | 71.49 |

Individual CPU observations are 76.371, 71.155, 58.206 and 51.686 seconds on
the shared host. The first pair improves by 6.83%, but the complete series
changes by +1.02% and fails the predefined 5% CPU-reduction gate. Testing stops
at that gate; the rolling window is not adopted. The measured memory increase
is acceptable, but this series establishes no sustained CPU improvement.

These measurements do not establish a new FFmpeg ratio. The last complete
external comparison is recorded in [CABAC performance](h264-cabac-performance.md).

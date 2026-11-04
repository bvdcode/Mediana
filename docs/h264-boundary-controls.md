# H.264 macroblock boundary classification

Two boundary-strength organizations were compared with the current decoder.
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

Separate masks fail the requirement of at least 5% CPU reduction in both orders.
The paired calculation passes that requirement. Its peak RSS medians are
71.54 to 80.93 MiB in ABBA and 71.52 to 71.74 MiB in BAAB.

The paired calculation also completes sixteen exact CAVLC/encoding controls.
CAVLC CPU changes by -0.28% and -0.06%. Encoding changes by -2.20% and +4.12%;
the reverse order exceeds the predefined 3% regression allowance. Therefore
the general paired replacement fails the complete acceptance criteria.

Both calculations pass 524,288 independent sample-coordinate topology cases,
264,192 further topology cases and physical-reference, mutation and range
checks in managed, software and Native AOT execution. The paired calculation
adds 4,800 range/error cases, including partial writes and repeated-call
allocation checks. Each also passes 729 H.264 tests in both managed modes.

These measurements do not establish a new FFmpeg ratio. The last complete
external comparison is recorded in [CABAC performance](h264-cabac-performance.md).

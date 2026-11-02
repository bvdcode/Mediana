CAVLC level fields are selected directly from the code and current suffix
length. Prefix, stop bit and suffix are appended through one bit-field write.
The previous implementation enumerated prefixes from zero through fifteen and
made separate writes for the zero prefix, stop bit and suffix.

The derivation follows H.264 (06/2026), clauses 9.2.2 and 9.2.2.1
([specification](https://www.itu.int/rec/T-REC-H.264-202606-I/en)). Prefix 14
with suffix length zero carries four suffix bits; prefix 15 carries twelve.
Baseline syntax keeps the existing maximum prefix of fifteen. The suffix
adaptation, level signs, first-level adjustment and coefficient/run tables are
unchanged. A rejected level adds no bits. No bitstream profile, encoder decision,
quantizer, buffer, CPU instruction requirement or dependency changes.

Independent exhaustive checks enumerate the normative prefix ranges. All 65,536
level codes are checked at each of seven suffix lengths and all eight initial
bit alignments. They compare acceptance, every emitted bit, bit positions,
rejection without output, complete RBSP termination and released media storage.
The checks pass in managed execution, with hardware intrinsics disabled, and
in a strict Linux x64 Native AOT consumer.

Earlier [direct-codeword variants](h264-encoder-work.md#direct-cavlc-level-experiment)
did not establish a repeatable benefit at their source and build baseline.
The present implementation selects and writes fields within the existing writer
method, without a separate codeword type or an explicit inlining attribute.
The following comparison uses the current Speed compilation setting; it does
not isolate the effects of compilation preference from implementation changes.

Native disassembly confirms that level coding has no backward branch and makes
one bit-writer call instead of three. Its code size changes from 184 to 163
bytes.

## Complete encoding measurements

Frozen Linux x64 Native AOT consumers process all 1,441 pictures of the
60.041667-second, 1280x720, 24 fps raw input described in the
[stage comparison](h264-stage-performance.md). The baseline has product sources
at `7a4f781`. Both versions use `OptimizationPreference=Speed`, the default
instruction set, logical CPU 2 and `DOTNET_PROCESSOR_COUNT=1`. No owned build,
test or other media operation overlaps timing. All observations are retained.

| Execution order | Before CPU, s | After CPU, s |
| --- | ---: | ---: |
| before-1 | 39.326981 | |
| after-1 | | 37.082471 |
| after-2 | | 46.212063 |
| before-2 | 51.064918 | |
| after-3 | | 50.294299 |
| before-3 | 59.672395 | |
| before-4 | 52.156095 | |
| after-4 | | 48.668207 |

| Median process resource | Before | After |
| --- | ---: | ---: |
| CPU, s | 51.610506 | 47.440135 |
| Elapsed, s | 54.385406 | 49.344929 |
| Peak RSS, KiB | 36,146 | 36,660 |
| Cumulative managed allocation, bytes | 2,283,451,564 | 2,283,405,680 |

Encoding CPU decreases by 8.08% across the four observations per version.
The before/after/after/before series gives 7.85%; the reversed series gives
11.51%. Absolute CPU varies substantially on the shared host, including runs
of the same binary. These observations establish a local improvement rather
than a universal percentage. Peak RSS and allocation do not establish a memory
reduction.

Every operation reaches EOF after exactly 1,441 input pictures and encoded
packets, with zero retained media storage. All eight MPEG-TS outputs contain
78,332,832 bytes and have SHA256
`d3090ee699cfdcc4330ec9815adaa1e7b08de1d95530e2e7af2e1596a399e543`.
The identical bytes retain the independently verified samples, timing, quality
and bitrate of the preceding reference. Source, fixture, input and executable
hashes remain unchanged during measurement.

This change does not establish a new FFmpeg ratio or complete-film HLS result.
The remaining CPU and coding-efficiency gaps stay open.

## Validation

Release builds with zero warnings and errors. All 3,075 managed tests and 709
H.264 tests with hardware intrinsics disabled pass. All 30 packages pass assembly,
metadata and package-only dependency checks. The strict Linux x64 Native AOT
package consumer passes its complete test run; the sample and CLI also publish
with Native AOT. Formatting and license-header checks pass.

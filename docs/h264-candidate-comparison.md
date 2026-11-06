Complete H.264 encoding was measured with joint fractional-candidate scoring
and delayed prediction materialization. The variant preserves the existing
search order, YUV error objective, vector tie rule, interpolation and encoded
output. It was not adopted: the measured CPU reduction is small and memory
does not improve.

## Work census

An instrumented Native AOT consumer encodes the complete 1,441-picture,
1280x720, 24 fps input from the [stage comparison](h264-stage-performance.md).
The duration is 60.041667 seconds. Its complete output matches the independently
decoded 78,332,832-byte MPEG-TS reference.

| Prepared candidate operation | Evaluations | Evaluated rows | Mean rows per evaluation |
| --- | ---: | ---: | ---: |
| Read one prepared plane | 40,224,827 | 494,114,633 | 12.284 |
| Average two prepared planes | 40,192,757 | 560,273,556 | 13.940 |

These are 80,417,584 candidate evaluations and 1,054,388,189 processed rows.
The existing implementation writes each evaluated prediction row into temporary
storage while accumulating SAD and testing its rejection threshold. The census
also counts 52,187,943 chroma comparisons and 7,478,090 fractional improvements;
the latter includes final predicted-vector checks outside the prepared
neighborhood. These counts do not measure memory bandwidth or CPU shares.

## Joint scoring variant

The variant borrows prepared sample views and scores four candidates together.
It accumulates vector SAD components over complete blocks, reduces totals after
the row loop, then considers candidates in their original order. Chroma checks
and ties use the current best error. Only a selected candidate materializes its
luma prediction. No reference-picture cache or heap workspace is added.

The [x264 motion search](https://github.com/mirror/x264/blob/master/encoder/me.c)
provides an example of grouping candidate comparisons. The measured variant
retains Mediana's search decisions rather than adopting another encoder's
search policy.

Default Native AOT compilation on this x64 host reports `Avx2.IsSupported` as
false. The variant therefore uses its explicit SSE2 accumulation path in all
timed native observations. Disassembly confirms four `psadbw` operations and
vector accumulation within each row iteration, with scalar totals formed after
the loop. An ordinary managed control executes the AVX2 path; another managed
control disables hardware intrinsics. No minimum processor requirement changes.

Independent direct-convolution checks cover 6,048 grouped candidates, all
refinement phases, borders, extreme coordinates, rounding and maximum SAD.
They verify every materialized pixel, input and output guards, unchanged
reference/workspace storage and allocation-free reuse. All 188 retained complete
motion searches preserve their vectors and prediction hashes. These checks pass
in all three execution configurations.

## Complete native comparison

Both Native AOT consumers use product sources at `edb7bae`, with the described
variant applied only to its consumer. They run sequentially on logical CPU 2,
with one processing thread and `DOTNET_PROCESSOR_COUNT=1`, using SDK 10.0.402.
No build, test or other owned media workload overlaps timing. The second series
reverses implementation order. CPU is user plus system time.

| Execution order | Existing CPU, s | Variant CPU, s |
| --- | ---: | ---: |
| existing-1 | 39.993085 | |
| variant-1 | | 39.013426 |
| variant-2 | | 38.867134 |
| existing-2 | 39.475728 | |
| variant-3 | | 39.364625 |
| existing-3 | 39.947458 | |
| existing-4 | 39.540617 | |
| variant-4 | | 39.054755 |

| Resource median | Existing | Variant |
| --- | ---: | ---: |
| CPU seconds | 39.744038 | 39.034091 |
| Elapsed seconds | 42.480351 | 40.874611 |
| Peak RSS, MiB | 35.955 | 36.678 |
| Cumulative managed allocation, bytes | 2,283,428,444 | 2,283,411,520 |

The aggregate CPU reduction is 1.79%; the first and reversed series show 2.00%
and 1.34%. Peak RSS does not decrease, and cumulative allocation is effectively
unchanged. This result does not justify the additional grouping and view code
for the current performance work. Removing these temporary writes alone does
not prove a substantial reduction in complete encoding cost: this experiment
also changes grouping and evaluates complete candidate costs rather than
stopping after a row exceeds the threshold. It does not isolate the cost of
temporary stores from these other changes.

Every observation reaches EOF with zero retained media storage and produces
exactly 1,441 packets. All eight transports have SHA256
`d3090ee699cfdcc4330ec9815adaa1e7b08de1d95530e2e7af2e1596a399e543`.
The identical output retains the preceding
independent full-picture quality and timestamp verification.

The current Native AOT implementation was measured at product revision `ad431b1` using
complete decoding and encoding operations. The product and configuration files match the
[checked-span implementation](h264-frame-memory.md#synchronous-reconstruction-access).
The diagnostic adds elapsed timers and call counters around named codec methods. It does
not change codec decisions, storage contracts or output bytes.

## Input and controls

Each operation processes all 1,441 pictures of the same 60.041667-second
1280x720, 24 fps source used by the [stage comparison](h264-stage-performance.md).
Decoding demuxes the MOV source and writes all 1,992,038,400 YUV bytes.
Encoding reads that raw reference and writes 78,332,832 MPEG-TS bytes with
QP26, a six-second key-frame interval and the existing 10 Mbit/s rate budget.

Both versions use .NET SDK 10.0.402, Linux x64 Native AOT, Speed compilation,
the default instruction set, logical CPU 2 and `DOTNET_PROCESSOR_COUNT=1`.
Ordinary and diagnostic operations run serially in ordinary/diagnostic/
diagnostic/ordinary order for each stage. CPU is user plus system time; RSS is peak resident
process memory. The host is shared.

| Operation | Version | CPU, s | Elapsed, s | RSS, KiB |
| --- | --- | ---: | ---: | ---: |
| decode 1 | Ordinary | 18.445219 | 19.488061 | 67,480 |
| decode 2 | Diagnostic | 18.862734 | 19.336469 | 70,216 |
| decode 3 | Diagnostic | 19.862546 | 20.855098 | 69,052 |
| decode 4 | Ordinary | 18.513165 | 18.868715 | 68,952 |
| encode 1 | Ordinary | 34.282346 | 37.758543 | 38,732 |
| encode 2 | Diagnostic | 35.024947 | 36.862628 | 39,116 |
| encode 3 | Diagnostic | 34.871489 | 37.439181 | 38,940 |
| encode 4 | Ordinary | 34.152958 | 38.884609 | 38,260 |

CPU medians are 18.479192 seconds ordinary versus 19.362640 diagnostic for
decoding, and 34.217652 versus 34.948218 for encoding. Instrumentation and
scheduling affect these observations. This comparison measures diagnostic
perturbation; it does not establish an optimization or a new FFmpeg ratio.

## Decoding computation

The table gives medians of the two diagnostic observations. These are elapsed
intervals, including timer overhead and scheduling, rather than CPU percentages.
Slice totals include their prediction and residual operations. Intra reading
also includes chroma reconstruction; deblocking includes classification and
pixel filtering. Nested rows overlap and must not be added.

| Timed method group | Calls in each observation | Elapsed, s |
| --- | ---: | ---: |
| CAVLC I/P slices | 6,248 | 9.504 |
| CAVLC B slices | 5,280 | 5.474 |
| P prediction and motion syntax | 1,480,747 | 1.759 |
| P skip prediction | 934,786 | 0.476 |
| B prediction and motion syntax | 2,337,777 | 3.739 |
| Inter luma residual reading and reconstruction | 4,753,310 | 3.860 |
| Chroma residual reading and reconstruction | 5,187,600 | 2.058 |
| Intra macroblock reading and reconstruction | 434,290 | 2.919 |
| Deblocking ranges | 11,528 | 3.280 |
| Boundary classification, within deblocking | 3,888,000 | 1.615 |
| Pixel filtering, within deblocking | 11,664,000 | 1.358 |

The source contains eight slices per picture: 6,248 I/P and 5,280 B slice
calls, or 11,528 in total. This input exercises CAVLC; its CABAC call count is
zero. Total diagnostic operation elapsed time is 20.042 seconds, including
container reading and raw output. Median GC pause duration is 0.118 seconds.

These observations identify substantial prediction, residual and filtering
work. The prediction rows include syntax and reference/motion handling as well
as interpolation, so they do not establish interpolation alone as the cause.

## Encoding computation

All 1,441 pictures enter picture encoding once. There are 1,430 inter picture
attempts and 11 intra picture attempts; this input does not retry a complete
picture. Motion selection runs once per inter macroblock.

| Timed method group | Calls in each observation | Elapsed, s |
| --- | ---: | ---: |
| Complete picture encoding | 1,441 | 34.209 |
| Inter picture encoding | 1,430 | 33.302 |
| Intra picture encoding | 11 | 0.847 |
| Motion selection | 5,148,000 | 14.916 |
| Inter luma transform and quantization | 5,021,657 | 1.609 |
| Chroma transform and quantization | 5,061,257 | 1.005 |
| Inter luma coefficient writing and reconstruction | 5,148,000 | 9.644 |
| Chroma coefficient writing and reconstruction | 5,187,600 | 2.931 |

These intervals are also nested elapsed observations. Motion selection and
inter luma writing are within inter picture encoding. Shared chroma helpers
also serve intra pictures. Luma writing includes CAVLC coding, inverse
transformation, reconstructed samples and metadata updates; this measurement
does not attribute its complete interval to any one of those operations.

Total diagnostic operation elapsed time is 37.147 seconds, including raw input
and MPEG-TS output. Median GC pause duration is 0.123 seconds.

## Result and validation

The next computation targets are motion selection and coefficient writing/
reconstruction, followed by the decoder prediction and residual paths.
The measured [allocation intervals](h264-allocation-cost.md) and the rejected
[metadata-borrowing control](h264-frame-memory.md#per-operation-metadata-borrowing-control)
do not justify prioritizing a new buffer pool or another owner-access rewrite.
No parallel execution or codec algorithm change is introduced by this diagnostic.

All eight operations reach EOF with 1,441 pictures and zero owned media bytes.
Every raw output matches the independently decoded reference, SHA256
`1ae555fcff80cdedd1f41a1d1b9aa86aadcb23884b1e11305d6c1ab5191d2bca`.
Every encoded output matches the previously independently verified MPEG-TS
reference, SHA256
`d3090ee699cfdcc4330ec9815adaa1e7b08de1d95530e2e7af2e1596a399e543`.
Both diagnostic repetitions have identical call counts.

The instrumented binary publishes with Native AOT warnings treated as errors.

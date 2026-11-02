The latest [edge-filtering comparison](h264-edge-filtering.md) uses
24.706 CPU seconds for complete HD decoding versus 4.031 for optimized
FFmpeg, with identical raw samples (6.13x). These are medians of two complete
60.041667-second observations per implementation. Paired CPU reductions for this
change are 8.64–10.38%; elapsed and RSS observations are mixed. The preceding
[bit-reading comparison](h264-bit-reading.md) measures a separate change.
The [block interpolation comparison](h264-block-interpolation.md) measures raw
encoding at 45.955 versus 14.218 CPU seconds (3.23x); the external reference
remains smaller with higher PSNR. The stages are measured separately.
The latest [complete-film audiovisual HLS comparison](hls-hd-encoding.md)
uses 992.938 CPU seconds versus 197.567 for FFmpeg, a 5.03x gap,
with all 406 published files identical to their independently verified references.
Those full-film measurements precede the edge-filtering change.

The complete 60.041667-second HD input was measured separately through video
decoding and video encoding. Separating the stages identifies where the current
implementation spends more CPU than FFmpeg without mixing in resizing, audio
processing or two-rendition HLS output.

## Input and measurement

The input is the 1,441-picture 1280x720, 24 fps cut used by the
[fractional-motion comparison](h264-fractional-motion.md). Its SHA256 is
`227becb9f6f3d2803eaa93095b692ccea9110ca00d78e65872e812d3c00b1692`.
The source uses eight CAVLC slices per picture and includes inter prediction.
Its Main profile does not imply CABAC entropy coding.

The initial Mediana baseline is `098b902`; its production code matches `8b583aa`. A dedicated
Native AOT consumer uses the public container, video session, buffer ownership,
encoder and MPEG-TS APIs. It was published with .NET SDK 10.0.402 and strict AOT
warnings. The external implementation is FFmpeg 6.1.1-3ubuntu5 with libx264.
All timed processes ran sequentially on logical CPU 2 of the same Linux x64
host, with one decoder/filter/encoder thread and `DOTNET_PROCESSOR_COUNT=1` for
Mediana. No owned build, validation or other media workload overlapped timing.
Each row is one complete process observation on a shared host.

Decoding includes demuxing, source decoding and writing all visible YUV420P8
samples to a new raw file. The two outputs have exactly 1,992,038,400 identical
bytes. Encoding reads that same independent raw file, produces one 720p MPEG-TS
output and includes input frame storage and output writing. It does not decode
the original video, resize pictures or process audio.

Both encoders receive 1,441 pictures with exact 1/24-second timing and use
Baseline level 4.1, no B pictures, a six-second GOP, a 10 Mbit/s setting and a
20 Mbit buffer setting. Mediana starts at QP26; libx264 uses `veryfast`, disabled
scene-cut keyframes, and a 10 Mbit/s maximum rate. The packet-envelope and VBV
models and quality are different; this is not equal-quality throughput.

## Complete stage resources

CPU is user plus system time; RSS is process peak resident memory.

| Stage | Mediana CPU, s | FFmpeg CPU, s | CPU ratio | Mediana elapsed, s | FFmpeg elapsed, s | Mediana RSS, MiB | FFmpeg RSS, MiB |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Decode to raw YUV | 49.368 | 3.203 | 15.41x | 56.412 | 3.205 | 86.38 | 65.41 |
| Encode the same raw YUV | 86.273 | 13.813 | 6.25x | 89.418 | 14.493 | 50.34 | 112.59 |

The excess CPU in these operations is 46.164 seconds for decoding and 72.460
seconds for encoding. The lower encoding RSS does not extend to this decode
measurement. Both Mediana operations reach EOF and release their media storage.
These isolated stages do not replace the [complete-film HLS comparison](hls-hd-encoding.md).

Both encoded outputs were independently decoded through all 1,441 pictures.
Every transport PTS/DTS follows the exact 1/24-second cadence. PSNR compares
every decoded visible YUV sample with the same raw input.

| Encoder | MPEG-TS bytes | Video packet bytes | Aggregate YUV PSNR |
| --- | ---: | ---: | ---: |
| Mediana | 78,332,832 | 76,055,926 | 45.094 dB |
| libx264 veryfast | 75,383,112 | 73,423,334 | 46.098 dB |

The Mediana result uses more bytes and has greater squared error on this input.

## Diagnostic CPU dispatch controls

Additional complete FFmpeg processes changed only CPU dispatch options and
logging verbosity. These controls investigate implementation costs; the normal
optimized runs above remain the competitive comparison.

The decoder control uses [`-cpuflags 0`](https://ffmpeg.org/ffmpeg.html).
The encoder control passes `asm=0` through `-x264-params`; libx264 reports no
selected CPU capabilities. The parameter's purpose is described in the
[x264 CPU override change](https://mailman.videolan.org/pipermail/x264-devel/2008-April/004419.html).
These settings disable the libraries' selected optimized CPU paths, not all
machine instructions that a C compiler can emit.

| External stage | Normal CPU, s | CPU dispatch disabled, s |
| --- | ---: | ---: |
| FFmpeg decode | 3.203 | 6.894 |
| libx264 encode | 13.813 | 87.357 |

The controlled decoder still produces identical raw pixels and uses 7.16 times
less CPU than Mediana. Disabling libx264's optimized CPU paths moves encoding
cost close to Mediana's measured 86.273 seconds. Its encoded bytes change:
75,441,016 transport bytes and 46.089 dB aggregate PSNR, verified over all
1,441 decoded pictures. This supports investigating the full set of encoding
compute kernels; an isolated filter change does not represent their combined
cost. It does not establish equivalent coding decisions or justify comparing
Mediana only with the disabled-optimization configuration.

## Native decoder work distribution

A separate instrumented Native AOT consumer measures coarse decoder stages.
All its decoded bytes match the uninstrumented result. It processes 11,528
CAVLC slices and 5,187,600 macroblocks: 4,753,310 inter and 434,290 intra,
including 2,815,576 skipped macroblocks. No CABAC path is exercised by this input.

| Instrumented stage | Elapsed seconds |
| --- | ---: |
| Inter prediction and its syntax | 17.858 |
| Deblocking | 16.401 |
| Inter luma residual reading and reconstruction | 7.732 |
| Chroma residual reading and reconstruction | 3.993 |
| Intra macroblocks | 3.596 |

These times include measurement overhead and scheduling; they are not CPU
percentages. Chroma time includes both inter and intra blocks, so it overlaps
intra time and the rows must not be summed. The complete diagnostic process
uses 50.918 CPU seconds and 55.938 elapsed seconds. Reported GC pause duration
is 0.147 seconds; this does not measure all GC CPU. The uninstrumented decode
allocates about 5.37 GB cumulatively despite bounded live media storage.

Of 76,052,960 inter luma 4x4 blocks, 64,033,197 have zero coefficients (84.20%).
Of 41,500,800 chroma 4x4 blocks, 34,752,408 have zero AC and scaled DC (83.74%).
At the measured baseline, reconstruction fills residual storage, copies prediction
blocks and performs per-pixel addition/clipping for those zero cases. Deblocking
also derives the same luma boundary classification again for chroma planes.
These are concrete candidates for eliminating work while retaining exact pixels.

The reference implementation separates absent, DC-only and full residual work
in [FFmpeg's H.264 reconstruction](https://ffmpeg.org/doxygen/6.1/h264idct__template_8c_source.html)
and shares boundary classifications in its
[deblocking implementation](https://ffmpeg.org/doxygen/6.1/h264__loopfilter_8c_source.html).
H.264 clauses 8.5 and 8.7 define the reconstruction and filtering results that
any corresponding change must preserve. The profile identifies work to remove. The implementation measurements below
do not establish a new complete-film HLS CPU ratio.


## Known-zero reconstruction

Known-zero blocks now copy prediction directly into picture storage while updating
coefficient counts and reconstruction availability. Whole zero-residual macroblocks
avoid all per-4x4 inverse transforms, temporary prediction copies and per-sample
addition/clipping. Partial 4x4 and 8x8 reconstruction uses the same operation;
nonzero DC/AC arithmetic and entropy decisions remain unchanged. No additional
heap storage or retained picture cache is introduced.

The baseline is the production code at `cc87c3c`. Frozen Native AOT consumers
processed the complete input sequentially with the same one-core settings.
Decode was repeated in reverse implementation order. Encoding used identical
raw input with the changed implementation first.

| Stage and execution order | Before CPU, s | After CPU, s | CPU reduction | Before elapsed, s | After elapsed, s | Before RSS, MiB | After RSS, MiB |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Decode, before then after | 51.192 | 45.034 | 12.03% | 56.098 | 47.866 | 83.64 | 86.65 |
| Decode, after then before | 49.641 | 44.294 | 10.77% | 56.808 | 49.159 | 85.50 | 85.92 |
| Encode, after then before | 91.733 | 89.080 | 2.89% | 94.979 | 95.265 | 51.07 | 50.59 |

All decoded bytes match the independent FFmpeg raw output. Both encoded transport
files match the previously independently decoded Mediana output byte for byte:
78,332,832 bytes, SHA256
`d3090ee699cfdcc4330ec9815adaa1e7b08de1d95530e2e7af2e1596a399e543`.
Every native run reaches 1,441 pictures and releases its media storage at EOF.
The repeated decode observations support a CPU improvement of about 11-12% on
this input. Encoding has one paired observation; its elapsed time did not improve.
There is no demonstrated RSS reduction or new ten-minute HLS measurement.

Validation: all 2,924 managed tests, 18 focused tests with hardware intrinsics
disabled, all 30 package checks, strict package-only Native AOT build and
execution, strict CLI Native AOT publication, license headers and whitespace
checks passed. The zero-macroblock regression covers all 52 QPs and four picture
corners, including surrounding pixels, counts, modes and borrowed input.
Existing reference fixtures were unchanged.


## Shared deblocking classifications

Deblocking now computes each active luma boundary classification once per
macroblock and shares it with the corresponding chroma edges, as specified by
H.264 clause 8.7.2. A 32-byte stack workspace covers both directions. Pixel
filtering order, slice boundary controls, 8x8 transform edge selection, and
independent Cb/Cr quantizers are preserved. Zero-strength edges and zero
alpha/beta thresholds skip pixel filtering.

The baseline is `f6c5ba0`, including known-zero reconstruction. Both complete
paired measurements use frozen Native AOT binaries and the same one-core
methodology. The second pair reverses implementation order.

| Execution order | Before CPU, s | After CPU, s | CPU reduction | Before elapsed, s | After elapsed, s | Before RSS, MiB | After RSS, MiB |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Before then after | 45.511 | 37.515 | 17.57% | 47.476 | 43.797 | 86.53 | 102.09 |
| After then before | 43.403 | 38.098 | 12.22% | 46.615 | 43.505 | 81.07 | 90.61 |

All four outputs contain the same 1,441 pictures and exactly match the independent
FFmpeg raw output. Every process reaches EOF with zero retained media storage.
CPU improves in both execution orders, but peak RSS increases in both pairs.
Cumulative managed allocations remain about 5.37 GB per process; the first pair
reports 344 versus 319 generation-2 collections. These observations do not
establish the cause of the higher RSS. There is no claim of a memory reduction.

Three fresh optimized FFmpeg decode processes used the same input and CPU
affinity after the native pairs. Each produced identical raw samples.

| FFmpeg observation | CPU, s | Elapsed, s | RSS, MiB |
| --- | ---: | ---: | ---: |
| 1 | 4.241 | 4.761 | 73.66 |
| 2 | 3.138 | 3.139 | 64.65 |
| 3 | 3.433 | 5.212 | 74.66 |

The ratio of the two Mediana and three FFmpeg CPU medians is 11.01x.
These small samples on a shared host show that the CPU gap remains large;
they do not establish a broad performance ranking or a new complete-film HLS
result. The changed filter is a decoder path, so no new encoder throughput
claim is made for this change.

Validation: Release build, all 2,928 managed tests, 38 focused checks with
hardware intrinsics disabled, all 30 package checks, strict package-only
Native AOT execution, CLI and measurement-consumer Native AOT builds, license
headers and formatting passed. New chroma tests cover both edge directions
and independent quantizer offsets; they also pass on the preceding filter.
All existing independent reference fixtures remain unchanged.

## Direct prediction candidate

A candidate changed prediction kernels to bounded strided output views and wrote
inter prediction directly into picture storage. It removed the partition-to-
macroblock and macroblock-to-residual prediction copies. It also changed shared
kernel calls used by encoding. The candidate was rejected: complete decoding did
not improve, and encoding used about 8% more CPU in both execution orders.
Production retains the preceding implementation at `a236473`.

The same frozen Native AOT consumers, complete 1,441-picture input, raw encoding
input and one-core settings were used. The second pair reverses the order of each
stage. No owned build, validation or media workload overlapped the timed processes.

| Stage and execution order | Before CPU, s | Candidate CPU, s | CPU increase | Before elapsed, s | Candidate elapsed, s | Before RSS, MiB | Candidate RSS, MiB |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Decode, before then candidate | 36.115 | 36.218 | 0.28% | 37.605 | 39.067 | 85.24 | 87.64 |
| Decode, candidate then before | 35.937 | 37.052 | 3.10% | 40.950 | 39.946 | 104.20 | 86.14 |
| Encode, candidate then before | 86.190 | 93.081 | 7.99% | 90.981 | 101.041 | 50.21 | 50.86 |
| Encode, before then candidate | 80.778 | 87.184 | 7.93% | 87.726 | 90.520 | 51.57 | 49.35 |

All eight native outputs are byte-identical to their preceding independent
references, reach EOF and release media storage. The candidate passed 2,949
managed tests, 688 H.264 tests with hardware intrinsics disabled, all 30 package
checks, strict Native AOT consumers and license checks. Correctness did not
establish a performance benefit. The measurements compare the whole candidate;
they do not isolate the cost of its view representation from its data flow changes.

A fresh optimized FFmpeg decode after these native processes used 3.249 CPU
seconds, 3.252 elapsed seconds and 64.55 MiB peak RSS, producing identical raw
samples. The two retained-implementation decode observations use 11.06-11.12
times as much CPU as this external observation. This remains a full-minute
decode comparison, not a new complete-film transcode result.

## Decoder kernel profile after work elimination

A separate instrumented Native AOT build of `a236473` decoded the same complete
input and produced identical raw samples. It used 39.590 CPU seconds and 40.961
elapsed seconds. Selected method timings include instrumentation and scheduling;
nested rows overlap and must not be summed or treated as CPU percentages.

| Instrumented operation | Calls | Elapsed seconds |
| --- | ---: | ---: |
| Luma prediction | 18,907,909 | 6.820 |
| Fractional luma interpolation, within luma prediction | 7,726,869 | 5.681 |
| Chroma prediction | 37,815,818 | 4.070 |
| Fractional chroma interpolation, within chroma prediction | 16,813,084 | 2.116 |
| Border copying, within integer and fractional prediction | 56,723,727 | 1.923 |
| Deblocking | 11,528 | 9.282 |
| Boundary classification, within deblocking | 3,888,000 | 5.903 |
| Pixel filtering, within deblocking | 11,664,000 | 3.093 |
| Inter luma residual reading and reconstruction | 4,753,310 | 4.353 |
| Chroma residual reading and reconstruction | 5,187,600 | 2.294 |
| Inverse 4x4 transform, within reconstruction | 23,468,260 | 1.619 |

Boundary classification includes repeated motion/reference metadata resolution.
For multislice pictures, reference identity resolution searches the slice list
for individual block comparisons. The profile supports investigating preparation
of those references and metadata at macroblock or slice granularity. It does not
establish a measured gain from that change.

General luma interpolation still selects its phase inside the sample loop.
FFmpeg uses [specialized fractional-pixel kernels](https://ffmpeg.org/doxygen/6.1/h264qpel__template_8c_source.html)
and separates [boundary classification and pixel filtering](https://ffmpeg.org/doxygen/6.1/h264__loopfilter_8c_source.html).
The remaining work concerns both data preparation and the complete compute path;
adding vector arithmetic to isolated helpers has not closed the measured gap.

## Prepared deblocking metadata

The boundary classifier now borrows its coefficient, mode, reference-index and
motion-vector spans once per filtering range. It resolves captured reference
lists at most once for each of the current, left and top macroblocks while
processing a macroblock. Both edge directions share that context. Reference
lookup remains lazy, after intra and residual classification, so those edges
do not require motion references. No per-picture mapping or heap buffer is added.

Reference identity, list swaps, quarter-pixel thresholds, 8x8 residual groups,
slice controls and pixel filtering order retain their existing semantics.
The comparison baseline is `a236473` and the changed implementation is `346f8cf`;
the preceding direct-prediction candidate
is absent from both implementations. Frozen Native AOT consumers ran on the
same full-minute input and one-core configuration in both execution orders.

| Execution order | Before CPU, s | After CPU, s | CPU reduction | Before elapsed, s | After elapsed, s | Before RSS, MiB | After RSS, MiB |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Before then after | 36.938 | 31.874 | 13.71% | 42.906 | 37.733 | 93.57 | 79.55 |
| After then before | 36.947 | 31.201 | 15.55% | 42.124 | 33.061 | 84.77 | 87.92 |

All four outputs match the independent raw reference byte for byte, contain
1,441 pictures and release media storage at EOF. CPU improves in both pairs;
RSS changes in opposite directions, so no consistent memory reduction is shown.
There is no new complete-film HLS or encoder throughput claim for this change.

The same diagnostic instrumentation also verifies the intended reduction in
boundary-classification work. Both instrumented results produce identical raw
pixels, with unchanged method call counts.

| Instrumented operation | Before elapsed, s | After elapsed, s |
| --- | ---: | ---: |
| Deblocking | 9.282 | 4.932 |
| Boundary classification, within deblocking | 5.903 | 1.578 |
| Pixel filtering, within deblocking | 3.093 | 3.072 |

These are nested elapsed observations including instrumentation overhead, not
CPU percentages. The separate whole-process comparisons above establish the
measured throughput improvement. Luma/chroma prediction still accounts for
11.245 instrumented elapsed seconds after this change.

Three fresh optimized FFmpeg decode processes produced the same complete raw
output. Each ran separately from owned compilation, validation and media work.

| FFmpeg observation | CPU, s | Elapsed, s | RSS, MiB |
| --- | ---: | ---: | ---: |
| 1 | 3.633 | 3.637 | 64.78 |
| 2 | 4.149 | 4.167 | 64.56 |
| 3 | 3.784 | 7.312 | 74.61 |

The ratio of the two changed-Mediana and three FFmpeg CPU medians is 8.34x.
These small shared-host samples still show a large CPU gap. The difference
from earlier external observations is not itself an improvement attributable
to Mediana; the paired native measurements quantify this implementation change.

Validation: Release build, all 2,930 managed tests, 669 H.264 tests with hardware
intrinsics disabled, all 30 package checks, strict package-only Native AOT
execution, native CLI build, license headers and formatting passed. Shared
managed/native vectors cover P/B reference changes at slice boundaries inside
rows, duplicate indices, list exchanges and exact filtered edge samples. Missing
reference checks preserve intra/residual precedence for explicit and captured
lists. Existing independent reference fixtures remain unchanged.

## Reused chroma interpolation rows

Complete eight-column chroma tiles now retain the unrounded horizontal result
from the preceding row. Each subsequent row calculates only its new horizontal
samples. The vertical stage combines the two rows and rounds once, preserving
the bilinear interpolation result. Partial tiles keep the scalar equation.
There is no additional heap storage or wider source read.

The comparison baseline is `6d1a01f`; the changed implementation is `715a575`.
The same frozen Native AOT consumers,
raw input, encoder settings and one-core configuration were measured in both
execution orders. CPU includes all 1,441 frames, input and transport output.

| Execution order | Before CPU, s | After CPU, s | CPU reduction | Before elapsed, s | After elapsed, s | Before RSS, MiB | After RSS, MiB |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Before then after | 85.918 | 72.974 | 15.07% | 87.421 | 75.528 | 49.06 | 49.47 |
| After then before | 83.797 | 71.796 | 14.32% | 97.500 | 76.993 | 51.47 | 51.54 |

Every MPEG-TS output is byte-identical to the encoded reference above. Motion
candidates, their selection, rate decisions and quality are unchanged. Each
process reaches EOF and releases its media buffers. Memory remains approximately
unchanged; the measurements do not demonstrate a reduction in peak RSS.

The same kernel is used by decoding. Paired full-input decode CPU observations
were 32.185 to 31.534 seconds and, in reverse order, 31.286 to 31.717 seconds.
All raw samples match the independent reference. These small differences have
opposite signs and do not establish a decoder throughput improvement.

A separate diagnostic encoder profile of the baseline measured 92.079 seconds
inside picture encoding, including 51.441 seconds in motion search and 36.454
seconds in its fractional refinement. These are nested elapsed observations
with instrumentation overhead, not CPU percentages. The full-process results
above establish the performance change. Search policy and the remaining
transform, quantization and reconstruction work still require improvement;
the complete-film HLS measurement has not been repeated for this change.

Two fresh optimized FFmpeg/libx264 processes used the original `veryfast`
configuration and one-thread restriction. Both complete transport outputs match
the independently decoded FFmpeg reference, including its quality and size.

| FFmpeg observation | CPU, s | Elapsed, s | RSS, MiB |
| --- | ---: | ---: | ---: |
| 1 | 12.610 | 12.616 | 110.99 |
| 2 | 12.729 | 12.733 | 109.59 |

The ratio of the changed-Mediana and fresh FFmpeg CPU medians is 5.71x. The
existing quality and bitrate differences still apply: the FFmpeg reference is
smaller and has higher PSNR. The encoder CPU gap remains substantial.

Validation: all 2,930 managed tests, 669 H.264 tests with hardware intrinsics
disabled, 30 package checks, the strict package-only Native AOT suite and native
CLI build passed. Release build, license headers and formatting also passed.
Existing shared managed/native vectors verify every chroma fraction, scalar
bilinear sampling, partial tiles, picture borders, extreme coordinates, output
guards and the complete motion-search decisions.

## Complete forward blocks and prepared quantization

The x64 forward transform now processes the complete 4x4 coefficient matrix in
SSE2 registers. Two transposes and two vector transforms replace eight scalar
line calls and repeated intermediate storage accesses. The input is fully read
before output writes, including for overlapping spans. Dispatch uses
[`Sse2.IsSupported`](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.intrinsics.x86.sse2.issupported)
and retains the existing scalar transform on other CPUs.

Forward quantization prepares its three scale factors, shift and rounding
constant once per block. A fixed scan-to-scale mapping replaces per-coefficient
coordinate classification. Chroma DC also shares its rounding constant within
the block. Signed rounding, coefficient order and error behavior remain exact.

The transform baseline is `fa73094`, with implementation `2093a6c`.
The quantization baseline includes that transform, with implementation `3e9e092`.
Each change was measured independently on all 1,441 frames with frozen native
consumers, the same raw input and the preceding one-core configuration.

| Change and execution order | Before CPU, s | After CPU, s | CPU reduction | Before elapsed, s | After elapsed, s | Before RSS, MiB | After RSS, MiB |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Transform: before then after | 68.843 | 66.239 | 3.78% | 69.896 | 69.626 | 50.43 | 51.61 |
| Transform: after then before | 70.986 | 69.535 | 2.04% | 72.149 | 71.610 | 48.57 | 50.57 |
| Quantization: before then after | 73.648 | 68.595 | 6.86% | 76.557 | 70.272 | 50.88 | 48.63 |
| Quantization: after then before | 73.238 | 67.086 | 8.40% | 76.419 | 68.629 | 52.62 | 51.50 |

These are separate paired comparisons on a shared host; their percentages must
not be added to estimate a combined speedup. All eight transport outputs match
the existing reference byte for byte, with EOF and zero retained media storage.
The changes add no heap buffers and do not alter the encoded quality or size.
No new complete-film HLS result is established by these stage measurements.

Validation passed all 2,942 managed tests, 681 H.264 tests with hardware
intrinsics disabled, 30 package checks, the strict package-only Native AOT
suite, native CLI, Release build, formatting and license headers. Shared native
and managed tests compare the transform with independent matrix multiplication
over valid residuals, full integer endpoints, random coefficients, overlapping
spans and output guards. Quantization checks every QP from 0 through 51, signed
rounding boundaries, 15/16-coefficient scans and chroma DC equations.

The updated diagnostic profile omits the per-SAD, per-neighborhood and nested
integer/fractional-search timers. Across the complete input, picture encoding
takes 67.005 instrumented elapsed seconds, including 30.295 in motion search,
12.309 in luma analysis, 5.738 in chroma analysis, 10.978 in luma writing and
3.329 in chroma writing. The complete output remains identical. These elapsed
timers overlap with their enclosing picture and inter-picture stages; they are
not CPU percentages and are not directly comparable with the more heavily
instrumented baseline. Motion search remains the largest measured operation.

Fresh optimized FFmpeg encoding observations after these changes used the same
one-thread settings and produced the complete independently verified reference:

| FFmpeg observation | CPU, s | Elapsed, s | RSS, MiB |
| --- | ---: | ---: | ---: |
| 1 | 12.513 | 12.681 | 113.27 |
| 2 | 12.303 | 12.304 | 111.97 |

The final Mediana and fresh FFmpeg CPU medians differ by 5.47x. The reference
quality and size differences still apply; these changes have not closed the
multi-fold encoder CPU gap.

## Combined fractional prediction and candidate cost

Implementation `e9b1e48`, measured against `1f1795c`, computes each fractional
luma prediction row and its sample error in one pass. Once the nonnegative
partial error exceeds the best complete candidate cost, remaining rows are
not generated. Such partial predictions are rejected before chroma evaluation
or winner storage. Equal-cost candidates retain their complete prediction and
the existing motion-vector tie rules. No search positions are removed.

The shared 16-byte difference operation uses guarded
[`Sse2.SumAbsoluteDifferences`](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.intrinsics.x86.sse2.sumabsolutedifferences)
on supported processors and retains the existing portable vector arithmetic
elsewhere. Native x64 disassembly confirms `PSADBW` in both the fused fractional
loop and the separate complete-block comparison. The change adds no heap
storage or external dependency.

Two paired complete 1,441-picture encodes used the same frozen inputs, CPU
affinity and single-thread settings. The second pair reversed execution order.

| Execution order | Before CPU, s | After CPU, s | CPU reduction | Before elapsed, s | After elapsed, s | Before RSS, MiB | After RSS, MiB |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Before then after | 64.666 | 64.444 | 0.34% | 65.665 | 65.789 | 51.21 | 49.80 |
| After then before | 67.453 | 63.741 | 5.50% | 68.636 | 64.149 | 49.80 | 51.80 |

Every complete output is byte-identical to the encoded reference, reaches EOF
and releases all media storage. The CPU reductions vary substantially; the
first elapsed observation slightly worsens, and RSS changes in both directions.
These observations do not establish a stable percentage improvement or a
memory reduction. The previous quality and size differences remain unchanged.

Validation passed all 2,944 managed tests, 683 H.264 tests with hardware
intrinsics disabled, all 30 package checks, the strict package-only Native AOT
suite and CLI, Release build, formatting and license headers. Shared managed
and native checks cover all 65,536 byte pairs, mixed vector lanes, all fractional
positions, scalar sample errors, rejection boundaries after every row, exact
ties, untouched output suffixes and the 188 independent motion-search cases.

The complete diagnostic encode takes 64.237 instrumented elapsed seconds:
27.177 in motion search, 12.451 in luma analysis, 5.807 in chroma analysis,
11.083 in luma writing and 3.350 in chroma writing. The diagnostic output remains
identical. These elapsed measurements include instrumentation and scheduling;
enclosing stages overlap, and the values are not CPU percentages. Substantial
work remains outside motion search as well as within it.

Two fresh optimized FFmpeg encodes used the same full raw input and one-thread
settings; both outputs match its independently decoded reference.

| FFmpeg observation | CPU, s | Elapsed, s | RSS, MiB |
| --- | ---: | ---: | ---: |
| 1 | 12.794 | 13.151 | 107.89 |
| 2 | 12.470 | 12.624 | 110.44 |

The current Mediana and FFmpeg CPU medians differ by 5.07x. This remains a
substantial encoding gap with different rate-control models and output quality;
the local change does not resolve it. There is no new decoder or complete-film
HLS measurement in this comparison.

## Shared source macroblocks

Implementation `241476e`, measured against `fc60e7c`, prepares each source
macroblock once in a 384-byte stack workspace. Motion search, inter and intra
residual analysis, intra-mode selection and PCM replacement share its samples.
The workspace belongs to the encoding call and does not retain the input frame.
Visible-edge replication, cropped dimensions and padded input row layouts are
resolved during preparation rather than for every subsequent 4x4 transform.

The residual transform gathers four bounded 32-bit rows from the prepared
source and its prediction into vector registers. It no longer copies each
4x4 source and prediction into separate temporary byte blocks. The row gather
is inlined; native x64 disassembly confirms that the two vector-return stack
round-trips are absent. Residual coefficient storage and transform arithmetic
remain unchanged. The measurements below cover the complete change, not an
isolated effect of inlining.

Both paired measurements process all 1,441 pictures using the same frozen
Native AOT consumers, independent raw input and one-core settings.

| Execution order | Before CPU, s | After CPU, s | CPU reduction | Before elapsed, s | After elapsed, s | Before RSS, MiB | After RSS, MiB |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Before then after | 65.013 | 60.802 | 6.48% | 68.071 | 61.995 | 51.22 | 50.62 |
| After then before | 64.950 | 60.501 | 6.85% | 66.103 | 62.908 | 51.80 | 51.04 |

All four outputs remain byte-identical to the independently verified encoded
reference, with 1,441 packets, EOF and zero retained media storage. CPU and
elapsed time decrease in both execution orders. Peak RSS remains approximately
51 MiB; the small differences do not establish a general memory reduction.

Validation passed all 2,945 managed tests, 684 H.264 tests with hardware
intrinsics disabled, all 30 package checks, the strict package-only Native AOT
suite and CLI, Release build, formatting and root license-header checks.
Independent matrix checks cover every source plane, dimensions from 1x1 through
35x33, visible borders, cropped macroblocks, padded rows, nonzero plane offsets
and distinct prediction strides. Input and output guards remain intact, and
repeated source preparation and transforms allocate no heap storage. Existing
motion-selection, intra-mode, PCM and HLS reference checks also pass.

The complete diagnostic encode takes 60.969 instrumented elapsed seconds:
0.932 in source preparation, 26.238 in motion search, 10.281 in luma analysis,
4.639 in chroma analysis, 11.122 in luma writing and 3.342 in chroma writing.
Source preparation was previously included in the motion-search timer and is
now measured separately; their costs must be considered together when comparing
the profiles. The diagnostic output remains identical. These elapsed timers
include instrumentation and scheduling, overlap their enclosing stages and
are not CPU percentages.

Fresh optimized FFmpeg processes used the same raw input and single-thread
settings; both complete outputs match its independently decoded reference.

| FFmpeg observation | CPU, s | Elapsed, s | RSS, MiB |
| --- | ---: | ---: | ---: |
| 1 | 13.251 | 13.265 | 108.96 |
| 2 | 13.324 | 13.963 | 107.37 |

The current CPU medians differ by 4.56x. FFmpeg timings also vary between
measurement batches, so changes in this ratio must not be attributed entirely
to Mediana. The paired 6.48-6.85% CPU reductions above measure this change
against its frozen baseline. The reference quality and size differences remain;
this is not equal-quality throughput and the encoding CPU gap remains open.

## Byte-chunk bit writing

`fb44a66` changes NAL bit-field writing from one iteration per bit to one
iteration per available byte portion. Partial-byte state, emulation prevention,
checkpoint restoration, overflow measurement and packet budgets are unchanged.
The existing storage and public API are retained.

The same complete 1,441-picture raw encoding ran in before/after and after/before
order with frozen Native AOT binaries and unchanged settings. The baseline
production code is `241476e`. Every output is byte-identical to the independently
decoded MPEG-TS reference; every operation reaches EOF and releases media storage.

| Execution order | Before CPU, s | After CPU, s | Before elapsed, s | After elapsed, s | Before RSS, MiB | After RSS, MiB |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Before, after | 61.956 | 60.437 | 63.108 | 60.947 | 50.71 | 51.16 |
| After, before | 60.766 | 59.427 | 60.846 | 61.381 | 50.68 | 48.73 |

CPU use decreased by 2.45% and 2.20%. Elapsed time improved in the first pair
and worsened slightly in the reverse pair; peak RSS changed in both directions.
The observations support a small CPU improvement, not a general memory reduction.
Two fresh optimized FFmpeg encodes used 12.626 and 13.568 CPU seconds, 12.638 and
14.070 elapsed seconds, and 110.63 and 112.40 MiB peak RSS. Their complete outputs
also match the independent reference. The ratio of median CPU observations is
4.58x; the external run variation and different encoding quality still apply.
The complete-film HLS comparison has not been repeated for this writer change.

All 2947 managed checks, 686 H.264 checks with hardware intrinsics disabled,
30 package checks, strict package-only Native AOT verification and the full native
consumer passed. Shared bit-writing checks cover all eight initial alignments,
all widths from zero through 64, mixed fields, independent NAL packing and
escaping, exact bit positions, budget overflow and restoration after storage
growth. Release build, CLI Native AOT, formatting and license-header checks passed.

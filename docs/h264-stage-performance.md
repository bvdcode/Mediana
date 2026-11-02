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

The Mediana source is `098b902`; production code matches `8b583aa`. A dedicated
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

# H.264 macroblock analysis

`H264EncoderOptions.MacroblockAnalysis` selects the prediction analysis policy:

- `Fast` is the default. Predictive pictures use motion-compensated Inter16×16 prediction with the existing skip decisions. It preserves the established encoded output and does not allocate rate-distortion state.
- `RateDistortion` combines a quantized P_SKIP probe, a smaller fractional-motion search and an additional Intra16 candidate in predictive pictures. It requires `H264EntropyCoding.Cabac`; requesting it with CAVLC throws `NotSupportedException` before encoding starts. It is an explicit quality/compression choice; operation cost depends on the content.

```csharp
H264EncoderOptions options = new()
{
    EntropyCoding = H264EntropyCoding.Cabac,
    MacroblockAnalysis = H264MacroblockAnalysis.RateDistortion,
    Quantizer = 26
};
```

RateDistortion uses four cardinal neighbors for each half-pixel and quarter-pixel refinement stage. Fast keeps its eight-neighbor refinement. The smaller search budget reduces motion-analysis cost while the subsequent reconstruction/rate decision retains most of the compression benefit. Neither policy changes interpolation or decoder reconstruction rules.

Before motion search, RateDistortion probes the derived P_SKIP vector when a previously coded left or top neighbor is skipped and the vector remains within the configured search range. The probe uses the existing interpolation and paired transform/quantization kernels. It accepts only when every quantized luma, chroma DC and chroma AC coefficient is zero, then writes the ordinary P_SKIP representation. A rejected probe leaves committed syntax, quantizer and reference state unchanged and continues through motion search. The probe uses bounded stack storage and allocates no managed memory. Fast does not execute this probe.

A zero quantized residual does not mean identical source samples or an optimal motion vector. Taking this early decision changes the reconstructed reference chain and can lower PSNR at the same QP. The resulting speed, size and quality trade is measured together below.

The additional analysis is bounded to macroblocks whose Inter16×16 candidate has nonzero luma residual in all four 8×8 coded groups. It shortlists one legal Intra16 mode by luma SAD, then compares both candidates using their actual clipped YUV reconstruction error and local CABAC arithmetic-interval cost. The comparison includes macroblock type, motion differences, coded-block pattern, quantizer syntax and residual bins from the same initial adaptive contexts. A partial cost can reject a candidate only when its nonnegative accumulated cost cannot beat the current candidate. Equal total costs retain the inter candidate.

The encoder prepares each candidate's 384 YUV420 samples once. A separate value-type arithmetic-cost backend estimates candidate costs from the same initial range and adaptive contexts, restoring trial macroblock state between estimates. It shares the output writer's bin syntax but writes no bytes. Only the winning candidate is committed to the output writer and reconstructed picture. No output rollback or repeated inverse transform is required. The output backend has no cost-estimation branch in its bin methods.

The decision is `SSE + λ × local_bits`, with `λ = 0.85 × 2^((QP − 12) / 3)`. This is the no-B P-slice base in the [JM reference encoder's `get_implicit_lambda_p_slice`](https://vcgit.hhi.fraunhofer.de/jvet/JM/-/blob/master/source/app/lencod/lambda.c), before optional metric, temporal-distance and context adjustments. It is an encoder policy, not a conformance requirement. [ITU-T H.264](https://www.itu.int/rec/T-REC-H.264-202606-I/en) specifies reconstruction and CABAC decoding; it does not require an encoder to use a particular rate-distortion search. Local interval cost is distinct from final escaped NAL or transport size.

The same option supports fixed QP and the existing media-time rate controller. Packet timing, keyframe decisions, cancellation and reset retain their existing contracts. Selecting `RateDistortion` does not enable B pictures, weighted prediction or additional motion partitions. [In-loop deblocking](h264-encoding.md#prediction-and-residual-coding) is selected independently through `Deblocking`.

Validation includes complete independently decoded sequences at QP 0, 12, 26, 38 and 51, cropped edges, chroma motion, scene changes, variable frame durations and adaptive quantizers. Mathematical checks cover 512 arithmetic checkpoint/replay sequences and 1440 prepared-reconstruction cases, including scalar execution.

## Measured compression and CPU tradeoff

A Linux x64 Native AOT comparison on an Intel Core i5-12450H used one execution CPU, CABAC, no B pictures, one reference picture and a 48-picture IDR interval. Each operation reads the complete YUV420 input, encodes every picture and muxes MPEG-TS. The source cases were natural video (1280×720, 288 pictures), motion (640×360, 192 pictures) and flat content (1280×720, 192 pictures), all at 24 pictures/s. Independent FFmpeg decoding checks complete picture counts, clocks and PSNR. Payload counts sum the Annex-B video packet sizes; transport overhead is excluded from this size comparison.

| Content | QP | Fast bytes | RateDistortion bytes | Fast PSNR | RateDistortion PSNR |
| --- | ---: | ---: | ---: | ---: | ---: |
| Natural | 22 | 4,423,007 | 3,446,899 | 45.839 dB | 46.100 dB |
| Natural | 26 | 2,993,947 | 2,135,179 | 43.110 dB | 43.181 dB |
| Natural | 30 | 2,209,758 | 1,436,354 | 40.804 dB | 40.710 dB |
| Natural | 34 | 1,740,728 | 987,866 | 38.577 dB | 38.139 dB |
| Motion | 22 | 1,622,865 | 1,564,534 | 49.763 dB | 49.321 dB |
| Motion | 26 | 1,290,795 | 1,230,209 | 45.634 dB | 44.984 dB |
| Motion | 30 | 1,007,237 | 945,883 | 42.735 dB | 42.378 dB |
| Motion | 34 | 767,046 | 696,219 | 39.595 dB | 39.070 dB |
| Flat | 22 | 38,880 | 32,420 | 51.386 dB | 51.315 dB |
| Flat | 26 | 34,600 | 30,896 | 53.969 dB | 53.895 dB |
| Flat | 30 | 35,068 | 28,588 | 44.506 dB | 44.460 dB |
| Flat | 34 | 45,564 | 28,448 | 40.427 dB | 40.232 dB |
Interpolating log payload size within the measured PSNR brackets at the Fast QP26 quality gives approximately **29.5% less payload for natural video** and **2.0% less for motion**. The motion estimate uses the additional QP25 point; neither estimate extrapolates. These are interpolation estimates, not claims that equal QP means equal quality. Flat content has a nonmonotonic QP/PSNR curve and is not fitted into a monotonic rate model. All twelve complete Fast transport hashes match the preceding implementation.

Compared with the preceding cardinal-refinement RateDistortion policy, the QP26 outputs change as follows:

| Content | Previous bytes / PSNR | Quantized-skip bytes / PSNR |
| --- | ---: | ---: |
| Natural | 2,356,786 / 43.416 dB | 2,135,179 / 43.181 dB |
| Motion | 1,265,628 / 45.869 dB | 1,230,209 / 44.984 dB |
| Flat | 34,580 / 53.969 dB | 30,896 / 53.895 dB |

At the preceding RateDistortion QP26 PSNR, interpolation between the measured new QP25/26 points estimates 6.0% less payload for natural video and 0.9% more for motion. Fixed-QP output therefore must not be described as preserving equal quality. The separate CPU comparison below retains the actual PSNR/size differences.

The common verification covers the updated RateDistortion references, exact Fast output hashes, zero-allocation repeated probes, every subpixel phase and coded-border extension. Complete generated sequences cover QP0/12/26/38/51, chroma-only changes, crop, variable cadence, reset, cancellation and adaptive rate retries. Probe checks independently compare scalar transform/quantization decisions and verify that rejected probes do not modify committed syntax or reference samples.

## Quantized-skip operation comparison

A separate final Native AOT comparison uses the preceding cardinal-refinement RateDistortion implementation as its reference. Each pair runs serially in ABBA order on one execution CPU, with two complete fresh processes per implementation. CPU includes Y4M reads, encoding, MPEG-TS muxing and process exit. Every complete transport hash matches the independently decoded quality result.

| Input | QP | Previous RD CPU | Quantized-skip RD CPU | CPU change | PSNR change | Payload change |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Natural | 26 | 4.823 s | 4.255 s | −11.8% | −0.235 dB | −9.4% |
| Motion | 26 | 1.011 s | 0.868 s | −14.2% | −0.885 dB | −2.8% |

Equal QP does not give equal quality. Two additional direct comparisons use the closest tested integer QP above the reference PSNR: new QP25 against previous QP26. These preserve the actual higher quality and larger payload rather than claiming an exact PSNR match.

| Input | Previous QP26 CPU | New QP25 CPU | CPU change | PSNR change | Payload change |
| --- | ---: | ---: | ---: | ---: | ---: |
| Natural | 4.821 s | 4.346 s | −9.9% | +0.764 dB | +6.1% |
| Motion | 1.074 s | 0.905 s | −15.7% | +0.852 dB | +4.7% |

Natural QP26 CPU samples are 4.821/4.824 s before and 4.216/4.294 s after; motion samples are 1.006/1.017 s versus 0.863/0.873 s. These short runs support a useful operation-level tradeoff; they do not establish a universal speed coefficient or x264 veryfast parity. Integer QP25/26 brackets do not prove exact matched-quality CPU.

At QP26, managed allocated bytes decrease from approximately 422.54 to 421.99 MB for natural video and 74.59 to 74.55 MB for motion. Median process peak RSS is 50.0 versus 56.6 MiB for natural video and 18.3 MiB for both motion variants. QP25 natural peak is 51.3 MiB. RSS includes executable, runtime and collection behavior; the probe retains no new picture or persistent cache. Repeated probe checks require zero heap allocation, unchanged allocator reservation and a positive allocation-counter control inside a checked no-GC interval.

Fast remains the default and retains all twelve complete transport hashes at QP22/26/30/34 on the three inputs. Native code inspection confirms that its macroblock specialization calls neither the quantized-skip probe nor rate-distortion selection. The quantized-skip policy changes only explicitly selected RateDistortion output.

The complete Native AOT test consumer builds with reference compatibility checks and compiler/linker warnings treated as errors. It passes the quantized-skip probe, complete RateDistortion references and reset/cancellation checks together with the remaining media pipeline tests.

## Bounded candidate evaluation and SIMD reconstruction

The current implementation estimates candidates before committing CABAC output,
uses SIMD for clipped sample reconstruction and reconstruction-error calculation,
and reuses the bounded SIMD SAD kernel for Intra16 mode selection. None of these
changes modifies prediction choices, quantized coefficients or complete encoded
bytes when deblocking remains disabled.

An independent Linux x64 Native AOT comparison used an Intel Core i5-12450H,
one logical execution CPU, CABAC/RD, a 60-picture GOP and a two-second rate buffer.
Each implementation ran four fresh processes per input, serially in ABBA order.
CPU includes the complete Y4M read, encoding, MPEG-TS output and process exit.

| Input | Pictures / rate | Nominal bit/s | Earlier CPU | Optimized CPU | Change |
| --- | ---: | ---: | ---: | ---: | ---: |
| HD30, 1280×720 | 150 / 30 | 4,000,000 | 5.870 s | 5.228 s | −10.9% |
| HD24, 1280×720 | 288 / 24 | 4,000,000 | 8.472 s | 7.830 s | −7.6% |
| Motion, 640×360 | 192 / 24 | 1,000,000 | 0.874 s | 0.825 s | −5.6% |
| Flat, 1280×720 | 192 / 24 | 4,000,000 | 1.060 s | 1.108 s | +4.5% |

Every complete transport SHA-256 remains identical. Flat content is a measured
regression, so these changes do not establish a universal speed improvement.
Median HD30 process peak RSS is 51.6 MiB before and 51.7 MiB after. Managed
allocation totals remain essentially unchanged with the same allocator; these
kernels add neither a picture buffer nor a per-candidate heap allocation.

The shared reconstruction kernel also improves decoding of three independent
x264 High-profile sequences. Four fresh processes per decoder/input give median
CPU of 2.495 → 2.380 s, 4.125 → 3.926 s and 0.649 → 0.593 s. All visible YUV
samples, including reordered pictures, match FFmpeg 6.1.1 exactly. Decoder RSS
still varies with collection timing; these measurements do not establish a
decoder memory reduction.

## Comparison at matched encoded payload

The same three video inputs were compared with FFmpeg 6.1.1 / libx264 `veryfast`,
one encoder thread and the same GOP and buffer duration. The reference retains
its normal B pictures and High-profile tools. Mediana uses CABAC/RD. Each Mediana
rate was adjusted until the sum of demuxed AVC packet bytes was within 1% of the
reference; matching nominal bitrates alone was insufficient. Transport overhead
is reported separately from encoded payload.

| Input | x264 payload | Mediana filtered payload | Difference | x264 PSNR | Mediana PSNR | x264 / Mediana SSIM |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| HD30 | 2,563,313 | 2,571,565 | +0.32% | 51.417 dB | 51.014 dB | 0.993616 / 0.992974 |
| HD24 | 6,073,119 | 6,078,200 | +0.08% | 52.129 dB | 49.009 dB | 0.996959 / 0.993201 |
| Motion | 1,076,721 | 1,076,756 | +0.003% | 47.926 dB | 44.256 dB | 0.998029 / 0.993413 |

The in-loop filter increases aggregate PSNR by 0.303, 0.109 and 0.664 dB over
the corresponding unfiltered Mediana encodes at matched payload. It operates on
the prediction references, rather than filtering only the exported image.
Zero-threshold exclusion preserves every encoded byte and avoids the complete
filter pass when the maximum possible luma/chroma thresholds cannot change a
sample. Mixed neighboring QPs, independent chroma offsets and threshold offsets
are checked across all 52 luma QPs.

Two final fresh processes per method/input, in reversed serial order, give the
following median CPU and median process peak RSS at those matched payloads.
Every output hash matches the independently decoded quality result above.

| Input | x264 CPU / RSS | Mediana unfiltered CPU / RSS | Mediana filtered CPU / RSS | Mediana filtered + pooled CPU / RSS |
| --- | ---: | ---: | ---: | ---: |
| HD30 | 2.805 s / 128.9 MiB | 4.643 s / 51.5 MiB | 5.000 s / 51.6 MiB | 4.913 s / 16.7 MiB |
| HD24 | 2.943 s / 129.3 MiB | 7.029 s / 50.5 MiB | 7.775 s / 50.5 MiB | 7.736 s / 18.0 MiB |
| Motion | 0.444 s / 71.5 MiB | 0.791 s / 13.5 MiB | 0.885 s / 13.3 MiB | 0.885 s / 11.6 MiB |

These are separate timing sessions from the earlier unchanged-output comparison;
cross-session differences are not an additional optimization coefficient. The
pooled configuration uses the same filter and produces the same encoded bytes
as the filtered unpooled configuration.

The HD30 result is close to the reference in these metrics. HD24 and motion
retain substantial quality gaps. Disabling B pictures, 8×8 transforms and smaller
inter partitions in x264 still leaves gaps; those absent tools alone do not
explain the result. Individual reference switches affect interacting decisions,
so their effects cannot be added together as independent contributions.
Motion estimation, intra-mode selection and rate allocation remain part of the
compression-efficiency work. x264's
[motion-estimation implementation](https://github.com/mirror/x264/blob/master/encoder/me.c)
documents its separate search/refinement budgets and transformed-error/rate
objectives.

## Thirty-second complete operation

A second series repeats the HD30 source six times: 900 pictures, 30 seconds,
1280×720 at 30 pictures/s. This measures sustained operation on repeated content,
rather than an additional independent scene. Each method runs twice in a reversed
serial order on the same execution CPU. The table reports median CPU and median
per-process peak RSS; no source decoding or image export is included.

| Method | CPU | Peak RSS | AVC payload | PSNR |
| --- | ---: | ---: | ---: | ---: |
| Earlier CABAC/RD, unpooled | 31.596 s | 51.6 MiB | 15,436,301 | 50.807 dB |
| Optimized CABAC/RD, unpooled | 27.892 s | 51.6 MiB | 15,436,301 | 50.807 dB |
| Optimized CABAC/RD, filtered, pooled | 29.643 s | 18.7 MiB | 15,438,852 | 51.080 dB |
| x264 veryfast | 17.143 s | 125.2 MiB | 15,239,065 | 51.435 dB |

The unchanged-output optimization reduces CPU by 11.7% on this complete sequence.
Both optimized modes process this workload faster than its media duration, with
little margin in the filtered case. The filtered payload is 1.31% larger than
x264, so this longer series is not an exact size match. The shorter series above
provides the tighter payload comparison. x264 remains faster in both series.

The pooled row explicitly uses the existing caller-owned
`MemoryPool<byte>.Shared` allocator. Managed allocation totals are approximately
1,335 MB for the unpooled operation and 17.8 MB for the filtered pooled operation;
the input-frame buffers account for most of this difference. This is an allocator
configuration comparison, not a claim that enabling deblocking removes those
allocations. Every encoder reports zero retained media-buffer bytes at EOF.
Repeated outputs have identical complete hashes within each configuration, and
the unfiltered earlier/optimized outputs are identical to each other. Independent
decoding processes the complete sequences for PSNR and SSIM.

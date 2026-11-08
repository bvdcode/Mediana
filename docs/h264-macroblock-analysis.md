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

The encoder prepares each candidate's 384 YUV420 samples once. A speculative commit and arithmetic-writer checkpoint allow retaining the winning candidate without repeating inverse transforms. A separate value-type arithmetic-cost backend shares the same bin syntax and context updates; it does not write output bytes or alter the committed candidate. The default output backend has no cost-estimation branch in its bin methods.

The decision is `SSE + λ × local_bits`, with `λ = 0.85 × 2^((QP − 12) / 3)`. This is the no-B P-slice base in the [JM reference encoder's `get_implicit_lambda_p_slice`](https://vcgit.hhi.fraunhofer.de/jvet/JM/-/blob/master/source/app/lencod/lambda.c), before optional metric, temporal-distance and context adjustments. It is an encoder policy, not a conformance requirement. [ITU-T H.264](https://www.itu.int/rec/T-REC-H.264-202606-I/en) specifies reconstruction and CABAC decoding; it does not require an encoder to use a particular rate-distortion search. Local interval cost is distinct from final escaped NAL or transport size.

The same option supports fixed QP and the existing media-time rate controller. Packet timing, keyframe decisions, cancellation and reset retain their existing contracts. Selecting `RateDistortion` does not enable B pictures, weighted prediction, additional motion partitions or in-loop deblocking.

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

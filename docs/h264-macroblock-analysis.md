# H.264 macroblock analysis

`H264EncoderOptions.MacroblockAnalysis` selects the prediction analysis policy:

- `Fast` is the default. Predictive pictures use motion-compensated Inter16×16 prediction with the existing skip decisions. It preserves the established encoded output and does not allocate rate-distortion state.
- `RateDistortion` can improve compression by considering an additional Intra16 candidate in predictive pictures. It requires `H264EntropyCoding.Cabac`; requesting it with CAVLC throws `NotSupportedException` before encoding starts. It adds CPU work and is an explicit quality/compression choice.

```csharp
H264EncoderOptions options = new()
{
    EntropyCoding = H264EntropyCoding.Cabac,
    MacroblockAnalysis = H264MacroblockAnalysis.RateDistortion,
    Quantizer = 26
};
```

The additional analysis is bounded to macroblocks whose Inter16×16 candidate has nonzero luma residual in all four 8×8 coded groups. It shortlists one legal Intra16 mode by luma SAD, then compares both candidates using their actual clipped YUV reconstruction error and local CABAC arithmetic-interval cost. The comparison includes macroblock type, motion differences, coded-block pattern, quantizer syntax and residual bins from the same initial adaptive contexts. A partial cost can reject a candidate only when its nonnegative accumulated cost cannot beat the current candidate. Equal total costs retain the inter candidate.

The encoder prepares each candidate's 384 YUV420 samples once. A speculative commit and arithmetic-writer checkpoint allow retaining the winning candidate without repeating inverse transforms. A separate value-type arithmetic-cost backend shares the same bin syntax and context updates; it does not write output bytes or alter the committed candidate. The default output backend has no cost-estimation branch in its bin methods.

The decision is `SSE + λ × local_bits`, with `λ = 0.85 × 2^((QP − 12) / 3)`. This is the no-B P-slice base in the [JM reference encoder's `get_implicit_lambda_p_slice`](https://vcgit.hhi.fraunhofer.de/jvet/JM/-/blob/master/source/app/lencod/lambda.c), before optional metric, temporal-distance and context adjustments. It is an encoder policy, not a conformance requirement. [ITU-T H.264](https://www.itu.int/rec/T-REC-H.264-202606-I/en) specifies reconstruction and CABAC decoding; it does not require an encoder to use a particular rate-distortion search. Local interval cost is distinct from final escaped NAL or transport size.

The same option supports fixed QP and the existing media-time rate controller. Packet timing, keyframe decisions, cancellation and reset retain their existing contracts. Selecting `RateDistortion` does not enable B pictures, weighted prediction, additional motion partitions or in-loop deblocking.

Validation includes complete independently decoded sequences at QP 0, 12, 26, 38 and 51, cropped edges, chroma motion, scene changes, variable frame durations and adaptive quantizers. Mathematical checks cover 512 arithmetic checkpoint/replay sequences and 1440 prepared-reconstruction cases, including scalar execution.

## Measured compression and CPU tradeoff

A Linux x64 Native AOT comparison on an Intel Core i5-12450H used one execution CPU, CABAC, no B pictures, one reference picture and a 48-picture IDR interval. Each operation reads the complete YUV420 input, encodes every picture and muxes MPEG-TS. The source cases were natural video (1280×720, 288 pictures), motion (640×360, 192 pictures) and flat content (1280×720, 192 pictures), all at 24 pictures/s. Independent FFmpeg decoding checks complete picture counts, clocks and PSNR. Payload counts sum the Annex-B video packet sizes; transport overhead is excluded from this size comparison.

| Content | QP | Fast bytes | RateDistortion bytes | Fast PSNR | RateDistortion PSNR |
| --- | ---: | ---: | ---: | ---: | ---: |
| Natural | 22 | 4,423,007 | 3,529,278 | 45.839 dB | 46.305 dB |
| Natural | 26 | 2,993,947 | 2,351,759 | 43.110 dB | 43.529 dB |
| Natural | 30 | 2,209,758 | 1,750,359 | 40.804 dB | 41.237 dB |
| Natural | 34 | 1,740,728 | 1,411,521 | 38.577 dB | 39.012 dB |
| Motion | 22 | 1,622,865 | 1,587,990 | 49.763 dB | 50.097 dB |
| Motion | 26 | 1,290,795 | 1,266,732 | 45.634 dB | 45.914 dB |
| Motion | 30 | 1,007,237 | 985,646 | 42.735 dB | 42.949 dB |
| Motion | 34 | 767,046 | 739,835 | 39.595 dB | 39.781 dB |
| Flat | 22 | 38,880 | 38,880 | 51.386 dB | 51.386 dB |
| Flat | 26 | 34,600 | 34,600 | 53.969 dB | 53.969 dB |
| Flat | 30 | 35,068 | 35,068 | 44.506 dB | 44.506 dB |
| Flat | 34 | 45,564 | 45,564 | 40.427 dB | 40.427 dB |

Interpolating log payload size within the measured PSNR brackets at the Fast QP26 quality gives approximately **25.6% less payload for natural video** and **4.2% less for motion**. This is an interpolation, not a claim that equal QP means equal quality. Flat content is byte-identical at each measured QP; its nonmonotonic QP/PSNR curve is not fitted into a monotonic rate model. All twelve complete `Fast` transport hashes also match the preceding encoder implementation.

The option improves compression at an additional analysis cost. In the paired QP26 motion runs, operation CPU was 0.953 s for Fast and 1.124 s for RateDistortion, approximately 18% higher. Natural Fast CPU varied from 5.08 to 6.22 s while RateDistortion measured 5.67–5.71 s, so that series does not establish a precise natural-video CPU percentage. These measurements do not demonstrate parity with x264 veryfast.

| QP26 operation | Fast allocated bytes | RateDistortion allocated bytes | Fast peak RSS | RateDistortion peak RSS |
| --- | ---: | ---: | ---: | ---: |
| Natural | 424,980,792 | 422,542,792 | 52,297,728 | 53,805,056 |
| Motion | 74,532,680 | 74,590,688 | 15,370,240 | 15,376,384 |
| Flat | 279,740,744 | 279,792,956 | 56,164,352 | 53,346,304 |

Memory entries are medians of the paired operation runs. Peak RSS includes the runtime, executable and buffers and varies between processes. RateDistortion adds bounded syntax state per predictive picture and borrowed stack storage per analyzed macroblock; it does not retain candidate pictures or duplicate whole-frame pixel buffers. Native code inspection confirms that the Fast macroblock specialization contains no call into rate-distortion selection.

# H.264 macroblock analysis

`H264EncoderOptions.MacroblockAnalysis` selects the prediction analysis policy:

- `Fast` is the default. Predictive pictures use motion-compensated Inter16×16 prediction with the existing skip decisions. It preserves the established encoded output and does not allocate rate-distortion state.
- `RateDistortion` can improve compression by considering an additional Intra16 candidate in predictive pictures. It requires `H264EntropyCoding.Cabac`; requesting it with CAVLC throws `NotSupportedException` before encoding starts. It is an explicit quality/compression choice; operation cost depends on the content.

```csharp
H264EncoderOptions options = new()
{
    EntropyCoding = H264EntropyCoding.Cabac,
    MacroblockAnalysis = H264MacroblockAnalysis.RateDistortion,
    Quantizer = 26
};
```

RateDistortion uses four cardinal neighbors for each half-pixel and quarter-pixel refinement stage. Fast keeps its eight-neighbor refinement. The smaller search budget reduces motion-analysis cost while the subsequent reconstruction/rate decision retains most of the compression benefit. Neither policy changes interpolation or decoder reconstruction rules.

The additional analysis is bounded to macroblocks whose Inter16×16 candidate has nonzero luma residual in all four 8×8 coded groups. It shortlists one legal Intra16 mode by luma SAD, then compares both candidates using their actual clipped YUV reconstruction error and local CABAC arithmetic-interval cost. The comparison includes macroblock type, motion differences, coded-block pattern, quantizer syntax and residual bins from the same initial adaptive contexts. A partial cost can reject a candidate only when its nonnegative accumulated cost cannot beat the current candidate. Equal total costs retain the inter candidate.

The encoder prepares each candidate's 384 YUV420 samples once. A speculative commit and arithmetic-writer checkpoint allow retaining the winning candidate without repeating inverse transforms. A separate value-type arithmetic-cost backend shares the same bin syntax and context updates; it does not write output bytes or alter the committed candidate. The default output backend has no cost-estimation branch in its bin methods.

The decision is `SSE + λ × local_bits`, with `λ = 0.85 × 2^((QP − 12) / 3)`. This is the no-B P-slice base in the [JM reference encoder's `get_implicit_lambda_p_slice`](https://vcgit.hhi.fraunhofer.de/jvet/JM/-/blob/master/source/app/lencod/lambda.c), before optional metric, temporal-distance and context adjustments. It is an encoder policy, not a conformance requirement. [ITU-T H.264](https://www.itu.int/rec/T-REC-H.264-202606-I/en) specifies reconstruction and CABAC decoding; it does not require an encoder to use a particular rate-distortion search. Local interval cost is distinct from final escaped NAL or transport size.

The same option supports fixed QP and the existing media-time rate controller. Packet timing, keyframe decisions, cancellation and reset retain their existing contracts. Selecting `RateDistortion` does not enable B pictures, weighted prediction, additional motion partitions or in-loop deblocking.

Validation includes complete independently decoded sequences at QP 0, 12, 26, 38 and 51, cropped edges, chroma motion, scene changes, variable frame durations and adaptive quantizers. Mathematical checks cover 512 arithmetic checkpoint/replay sequences and 1440 prepared-reconstruction cases, including scalar execution.

## Measured compression and CPU tradeoff

A Linux x64 Native AOT comparison on an Intel Core i5-12450H used one execution CPU, CABAC, no B pictures, one reference picture and a 48-picture IDR interval. Each operation reads the complete YUV420 input, encodes every picture and muxes MPEG-TS. The source cases were natural video (1280×720, 288 pictures), motion (640×360, 192 pictures) and flat content (1280×720, 192 pictures), all at 24 pictures/s. Independent FFmpeg decoding checks complete picture counts, clocks and PSNR. Payload counts sum the Annex-B video packet sizes; transport overhead is excluded from this size comparison.

| Content | QP | Fast bytes | RateDistortion bytes | Fast PSNR | RateDistortion PSNR |
| --- | ---: | ---: | ---: | ---: | ---: |
| Natural | 22 | 4,423,007 | 3,580,438 | 45.839 dB | 46.212 dB |
| Natural | 26 | 2,993,947 | 2,356,786 | 43.110 dB | 43.416 dB |
| Natural | 30 | 2,209,758 | 1,733,838 | 40.804 dB | 41.101 dB |
| Natural | 34 | 1,740,728 | 1,383,486 | 38.577 dB | 38.852 dB |
| Motion | 22 | 1,622,865 | 1,588,114 | 49.763 dB | 50.016 dB |
| Motion | 26 | 1,290,795 | 1,265,628 | 45.634 dB | 45.869 dB |
| Motion | 30 | 1,007,237 | 981,449 | 42.735 dB | 42.955 dB |
| Motion | 34 | 767,046 | 732,086 | 39.595 dB | 39.787 dB |
| Flat | 22 | 38,880 | 38,480 | 51.386 dB | 51.385 dB |
| Flat | 26 | 34,600 | 34,580 | 53.969 dB | 53.969 dB |
| Flat | 30 | 35,068 | 33,804 | 44.506 dB | 44.500 dB |
| Flat | 34 | 45,564 | 45,576 | 40.427 dB | 40.423 dB |
Interpolating log payload size within the measured PSNR brackets at the Fast QP26 quality gives approximately **24.4% less payload for natural video** and **3.9% less for motion**. This is an interpolation, not a claim that equal QP means equal quality. Flat content changes little at QP26; its nonmonotonic QP/PSNR curve is not fitted into a monotonic rate model. All twelve complete `Fast` transport hashes match the preceding encoder implementation.

A separate paired QP26 series ran the complete Native AOT operations serially in before/after/after/before order. Both modes retain the quality differences shown above; CPU values are not measurements at exactly matched PSNR.

| QP26 operation | Fast CPU | RateDistortion CPU | Fast allocated bytes | RateDistortion allocated bytes | Fast peak RSS | RateDistortion peak RSS |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Natural | 5.712 s | 5.640 s | 424,981,424 | 422,571,176 | 52,428,800 | 53,739,520 |
| Motion | 1.037 s | 1.149 s | 74,519,948 | 74,595,972 | 15,323,136 | 15,323,136 |
| Flat | 1.653 s | 1.481 s | 279,739,828 | 279,778,340 | 53,280,768 | 58,916,864 |

Entries are medians of two complete operations per mode. Natural CPU samples were 5.581/5.843 s for Fast and 5.681/5.600 s for RateDistortion; motion was 1.026/1.048 s versus 1.129/1.169 s. The measured motion analysis tax remains approximately 11%. Natural costs are similar; flat uses less CPU in this series. These measurements do not demonstrate parity with x264 veryfast.

Compared with eight-neighbor RateDistortion refinement, the cardinal refinement reduces observed CPU by approximately 8% on natural video and 6% on motion while increasing estimated matched-quality payload by approximately 1.6% and 0.2%, respectively. This is a search-budget tradeoff. Fast output remains unchanged.

Peak RSS includes the runtime, executable and buffers and varies between processes. RateDistortion adds bounded syntax state per predictive picture and borrowed stack storage per analyzed macroblock; it does not retain candidate pictures or duplicate whole-frame pixel buffers. Native code inspection confirms that the Fast macroblock specialization contains no call into rate-distortion selection.

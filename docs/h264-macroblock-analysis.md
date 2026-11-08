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

Validation includes complete independently decoded sequences at QP 0 through 51, cropped edges, chroma motion, scene changes, variable frame durations and adaptive quantizers. Mathematical checks cover 512 arithmetic checkpoint/replay sequences and 1440 prepared-reconstruction cases, including scalar execution.

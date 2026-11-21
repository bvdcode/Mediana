# ProRes decoding

Register `ProResDecoderFactory` with `Mp4ReaderFactory` to decode ProRes MOV files
through `VideoSession`, including indexed seeks. Each ProRes picture is intra
coded. The decoder supports Proxy, LT, Standard, HQ, 4444 and 4444 XQ, progressive
and field-interleaved pictures, quantization matrices and cropped edge slices.
Output is planar YUV422 at 10 bits or YUV444 at 12 bits. Alpha retains its full
16-bit precision in a separate straight-alpha plane; 8-bit alpha expands exactly.

`ProResStreamMetadataReader` reads geometry, precision, field order and color
from a bounded opening-packet header, without reconstructing pixels. The MP4
reader supplies the common indexed packet-prefix interface for all codecs.

Library decoding is strict by default. `RecoverLegacyAlpha` explicitly accepts
older encoders that pad the last alpha slice to sixteen rows or omit the final
single-sample run marker. Recovery reports a warning through the factory callback.
CLI enables this option and logs warnings. Normative alpha streams need no recovery.

Seven independent MOV encodings cover the six profiles and interlaced HQ.
Complete sequences and reverse seeks are checked against independent decoded
planes. Color samples agree within two code values after normalizing the reference
decoder's narrower clipping range. Alpha agrees exactly at the reference output
precision; direct packet tests check full 8/16-bit alpha values, backpressure,
retained output, truncation, cancellation and reset. Full-range color clipping
follows the standard; decoder accuracy qualification on the full Annex A corpus
remains open.

Normative reference: [SMPTE RDD 36:2022](https://pub.smpte.org/doc/rdd36/20220909-pub/rdd36-2022.pdf).

A Native AOT smoke measurement decodes and SHA-256 hashes all 60 frames of a
1280x720 HQ testsrc2 MOV (30 fps), using pooled buffers. On an i5-12450H, median
elapsed time over three fresh processes fell from 3.089 s to 0.905 s after
single-pass dequantization, a DC-only path and SIMD dot products. CPU fell from
3.092 s to 0.911 s. The final run allocated 8.96 MB of managed objects in total,
peaked at 25.7 MB RSS and retained no allocator leases.

FFmpeg 6.1.1, with one decoder/output thread and one filter thread, took 0.652 s
elapsed, 0.889 s CPU and 96.4 MB peak RSS on the same decode-and-hash workload.
FFmpeg measurements cover the whole process; Mediana's elapsed/CPU counters cover
the operation, and its RSS covers the process. This is a short synthetic workload,
not a general performance claim or a long-duration comparison.

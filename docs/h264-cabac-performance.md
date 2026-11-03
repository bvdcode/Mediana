At baseline `e96eb92`, complete High-profile CABAC decoding was measured separately from the existing
[CAVLC stage comparison](h264-stage-performance.md). Mediana uses 34.345 CPU
seconds versus FFmpeg's 5.204 seconds for this input, a 6.60x gap. Every decoded
sample and every Mediana presentation timestamp and duration is exact.

## Input and controls

The source contains 1,441 pictures at 1280x720 and 24 fps, lasting 60.041667
seconds. It was encoded from the same movie excerpt using libx264 `veryfast`,
CRF23, High profile, YUV420P8, a 144-picture key-frame interval, disabled
scene-cut keyframes and one encoding thread. The bitstream includes B pictures,
weighted prediction and 8x8 transforms. Independent PPS inspection confirms
`entropy_coding_mode_flag=1` and `transform_8x8_mode_flag=1`; profile alone does
not establish entropy coding. A time-code data track is present; there is no
audio track. Source SHA256:
`d973ecbbf7b76ae83d046df8e91ba627ed0dcd1abf7e1d4f3819dd5a25cc7fea`.

Both decoders demux the complete source and write all 1,992,038,400 visible
YUV420P8 bytes. The Mediana consumer checks each timestamp and duration using
exact rational comparisons, reaches EOF and releases all owned media storage.
The independently decoded raw reference has SHA256
`d4da435776e1e1fd96f1abe17558d17e7c69b5b81affdf5637582ff2908178f6`.

The builds use .NET SDK 10.0.402, Linux x64 Native AOT, Speed compilation and the
default instruction set. Measurements use logical CPU 2, one media-processing
thread, `DOTNET_PROCESSOR_COUNT=1` and sequential FFmpeg/Mediana/Mediana/FFmpeg
execution. No concurrent build, test or media operation runs during timing.
CPU means user plus system time; RSS is peak resident process memory. The host
is shared. These are complete decoding measurements, distinct from encoding,
audiovisual HLS and comparisons using the different CAVLC bitstream.

| Decoder | Observation | CPU, s | Elapsed, s | RSS, KiB |
| --- | --- | ---: | ---: | ---: |
| FFmpeg | 1 | 5.212634 | 5.217432 | 69,244 |
| Mediana | 1 | 34.115281 | 35.078897 | 62,952 |
| Mediana | 2 | 34.574531 | 37.074266 | 62,844 |
| FFmpeg | 2 | 5.195218 | 5.196980 | 71,832 |
| Mediana | Median | 34.344906 | 36.076581 | 62,898 |
| FFmpeg | Median | 5.203926 | 5.207206 | 70,538 |

Mediana's median RSS is 10.83% lower. The CPU gap remains substantial.
All four complete outputs match the independent raw reference byte for byte.
The Native AOT consumer builds with warnings treated as errors. Product code
is unchanged from the 3,198-test managed and strict Native AOT validation.

## Native stack observations

Two additional complete ordinary decodes surround two complete sampled decodes
of the same binary and source. Every output, timestamp and duration remains
exact. Ordinary CPU median is 33.969998 seconds; sampled CPU median is
34.070522 seconds. Sampling increases elapsed time from a 34.719039-second
ordinary median to 46.438093 seconds, so competitive timing uses only ordinary
operations.

There are 514 selected stack points: 152 in deblocking, 90 in prediction,
32 in CABAC arithmetic/context handling, 19 in inverse transformation,
10 in CABAC residual syntax, two in container/intra work and 209 elsewhere.
Selection chooses the thread with the largest positive CPU-tick change at each
stop. Waiting and runtime stacks remain included. These counts are diagnostic
observations rather than CPU percentages.

Deblocking reference resolution and strength classification have 40 and 35
leaf points respectively. Bidirectional weighting has 28; motion updates have
21. The decoder's next useful targets include metadata classification and
prediction kernels. The presence of CABAC alone does not justify assigning
the entire gap to entropy decoding or introducing slice workers.

## Resolved-motion reuse control

A separate fixed-size cache retains the two resolved reference/motion samples
for each of 16 current, four left and four top 4x4 blocks while classifying a
macroblock. It preserves physical reference identity, residual/intra priority,
list ordering and slice boundaries. Its 24 entries occupy 768 stack bytes per
deblocking range; it creates no per-macroblock arrays.

Four complete before/after/after/before Native AOT decodes preserve every raw
sample, timestamp and duration, EOF and zero owned media. CPU median changes
from 33.800783 to 32.158611 seconds, a 4.86% reduction. RSS medians are 62,950
and 63,020 KiB. This first series falls short of the predeclared 5% minimum CPU
reduction. The variant is not adopted; the remaining CAVLC and reverse-order
series are not run. The ordinary decoder and its storage contracts are
unchanged.

The subsequent [SIMD weighting comparison](h264-weighted-prediction.md) reduces
complete decoding CPU on the same CABAC input in both execution orders. It
measures a separate change and does not replace the ordinary FFmpeg comparison
above.


## Internal-index comparison control

A separate internal-edge shortcut checks equal reference indices and close
motion vectors within a macroblock before resolving full physical identities.
Intra/residual priority, swapped or duplicate references, unavailable entries
and slice edges remain independently checked in 93,312 cases in managed,
hardware-disabled and strict Native AOT execution.

Four complete CABAC decodes preserve every raw sample, timestamp and duration,
EOF and zero owned media. CPU median changes from 20.870151 to 20.426722 seconds,
a 2.13% reduction. This series does not meet its predeclared 5% threshold, so
the shortcut is not adopted and no reverse-order or CAVLC extension is run.
These are paired results on a shared host, not an update to the FFmpeg ratio.

The subsequent [CABAC history comparison](h264-cabac-history.md) reduces
auxiliary syntax storage from the complete picture to two macroblock rows.
Complete CABAC allocation traffic falls 15.35%; no sustained CPU improvement
is established for that change.

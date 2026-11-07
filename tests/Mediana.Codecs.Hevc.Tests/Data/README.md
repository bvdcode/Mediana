The four 64x48 synthetic HEVC videos contain twelve frames with B-picture reordering, in Main 8-bit and Main10 4:2:0. MP4 and Matroska retain the same encoded samples. They contain no external artwork or recordings.

Generation uses the independent x265 encoder through FFmpeg with `testsrc2=size=64x48:rate=12:duration=1`, the matching `yuv420p` or `yuv420p10le` pixel format, and `pools=none:frame-threads=1:wpp=0:keyint=12:min-keyint=12:bframes=2:repeat-headers=0`. MP4 uses the `hvc1` sample entry and a front-loaded movie index; Matroska is a stream copy of that file.

Each JSON reference is independent `ffprobe -show_streams -show_packets -show_data_hash sha256` output. Tests compare original compressed packet hashes and presentation timestamps, including reverse requests. MP4 decode timestamps are also compared; Matroska does not declare decode timestamps.

`weighted8.mp4` and `weighted10.mp4` contain 24 synthetic 192x128 pictures, three B pictures, multiple references and explicit luma/chroma weights. Generation adds `fade=t=in:st=0:d=2`, uses two-second duration, `bframes=3:weightp=1:weightb=1`; Main uses `pools=1:wpp=1`, Main10 uses `pools=none:wpp=0`. Other encoder options match the twelve-picture samples.

The four `.headers.json` files retain independently parsed SPS/PPS and slice fields from FFmpeg `trace_headers`, with each slice's final bit position after alignment. Seventy-two headers verify I/P/B syntax, reference counts, temporal prediction, Main/Main10 weight derivation and exact entropy offsets. The trace parser names the coded chroma offset delta `chroma_offset_l0/l1`.

`cabac.bin.gz` contains 129,888 independently encoded HM-18.0 arithmetic bins across 18 streams: all I/P/B context initializations, six quantizers, bypass/termination, and WPP checkpoints restored both locally and into another reader. Each case stores group sizes and initial packed probability states, the checkpoint operation index, then two arithmetic byte streams with three-byte `(group, index, symbol)` operations. Group 255 denotes bypass. Integers are little-endian 32-bit. Decompressed header: `HEVCCAB1`, followed by the case count.

SHA-256: `d0e2b52f509e3304f6f39cbc4cf711ed4eaadb48b21522fdc5464c44e045baae`.

`interpolation.bin.gz` contains 1,280 HM-18.0 predictions: both bit depths, every quarter-luma and eighth-chroma phase, rectangles from 4x4 through 64x16 and 48x48, interior and extended picture borders. The reference stores unrounded fourteen-bit values without the HM internal offset. Decompressed header `HEVCINT1` is followed by the 32-bit case count. Each case stores ten 32-bit integers (depth, chroma flag, source width/height, source x/y, fractional x/y, block width/height), unsigned 16-bit source samples and signed 32-bit output values. Fields are little endian.

SHA-256: `bbd3ce2b6d3c44b127a5b6a5cc42a39e512ce362afc3e9e62fb874b20300daf1`.

`weights.bin.gz` contains 2,592 HM-18.0 weighted prediction cases cover Main/Main10, all weight denominators, positive and negative weights, offset limits, clipping, unidirectional and bidirectional prediction. Header `HEVCWGT1`, case count, then eight signed 32-bit fields (depth, denominator, both weights, both offsets, bidirectional flag, sample count), pairs of positive fourteen-bit input samples and final integer predictions. Fields are little endian.

SHA-256: `301c1310cdb81c0427170ceb546f898f0ed0c5ee5f5c4be3e5e2d4e427fa8ea6`.

`motion.bin.gz` contains 768 HM-18.0 complete motion compensation cases cover both bit depths, both reference lists separately and together, default and explicit weights, fractional and extreme signed motion, rectangular blocks, chroma subsampling and border extension. Header `HEVCMCP1`, case count, then twelve signed 32-bit fields (depth, destination x/y, block width/height, both motion x/y vectors, direction, explicit-weight flag, denominator), six weight/offset pairs, and three component records. Each component stores two unsigned 16-bit 64x48 luma or 32x24 chroma source planes followed by signed 32-bit predicted block samples. Direction 0/1 selects one list; 2 selects both. Fields are little endian.

SHA-256: `5f17fe18efb247c4963d445a6cbf30647ece728eefad6d3e6b62afc674d32e78`.

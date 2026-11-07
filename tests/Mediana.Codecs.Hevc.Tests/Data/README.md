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

The four `.yuv.gz` files contain complete FFmpeg-decoded display-order Y, Cb, Cr planes from their matching videos, without filtering or resizing. Main uses byte samples; Main10 uses little-endian unsigned 16-bit samples. Tests compare every visible sample of all 72 pictures, including P/B prediction, weighted prediction, WPP, deblocking and SAO.

- `main8.yuv.gz` SHA-256: `0dded5a3bd5259a941f8a09295596ab26e837425138aaf8b131277ba7ade592e`.
- `main10.yuv.gz` SHA-256: `33271445002c0cfc413b821b97fd95d9349df085b2eeb920efe83fdb44f28f6c`.
- `weighted8.yuv.gz` SHA-256: `c8d131cd95306f3c04ff1d5b1a0f4dda5b28de7631baeabe0ea2f072931aa122`.
- `weighted10.yuv.gz` SHA-256: `2b6b11b18beed22700cbbc7dcd346ad9280628417ca3bc0284b74fa61c3d694b`.

The additional public-decoder sequences contain 136 pictures. `gop8` and `gop10`
use 188x126 visible samples, twelve-frame open GOPs, three B pictures, four
references and AMP/rectangular prediction; `gop8` declares SAR 4:3. Both use
`testsrc2` at 12 fps for four seconds, medium preset, `keyint=12:min-keyint=12:
scenecut=0:open-gop=1:bframes=3:ref=4:amp=1:rect=1:wpp=1:pools=none:
frame-threads=1`. `slices8` uses 256x256, 6 fps, two seconds, medium preset and
`pools=1:frame-threads=1:slices=2:wpp=1:keyint=6:min-keyint=6:scenecut=0:
open-gop=0:bframes=2`. Each picture has two independent slices. `predictive10`
uses 98x66, 10 fps, two seconds, ultrafast preset, `keyint=10:min-keyint=10:
scenecut=0:bframes=0:ctu=32:wpp=0:ref=1:pools=none:frame-threads=1`. `intra8`
uses 98x66, 4 fps, two seconds, medium preset and `keyint=1:ctu=32:wpp=0:
pools=none:frame-threads=1`. All five declare BT.709 primaries, transfer and matrix.
The matching compressed raw YUV references retain the same byte/word representation
as the first four sequences. Public tests compare all 208 pictures, source timing,
display reordering, independent output leases and repeated seeks.

- `gop8.mp4` SHA-256: `0f3aa8137475313ca888beb1ae7457c5fcc93b88a2831dfe851bad4cf984144f`.
- `gop8.yuv.gz` SHA-256: `4a0bb04696b33138064e9793884dc33e64655937e16b74323285e2ff9f06a39f`.
- `gop10.mp4` SHA-256: `e1b670c2ccf8c42e10984dbe8df5112c1a61f42f1a16834aeebf2cc951ec10cc`.
- `gop10.yuv.gz` SHA-256: `8f01f034c53116aa7e46b1ed4bb9e813797d4356a458f73a794823e114f02e8a`.
- `slices8.mp4` SHA-256: `15fa16aa6ed31f4c9a3518c8df366025cf3cdc83cc052a7fae57ecff359ca037`.
- `slices8.yuv.gz` SHA-256: `513fac093964acedc4c433df63736c779669a6d34cc1bf61617c16c0a985404d`.
- `predictive10.mp4` SHA-256: `2a576d64c3f120ed27344b2cce54e6019bf2ef9525f48b691c2d05472eb54f35`.
- `predictive10.yuv.gz` SHA-256: `64fdb0bad38e0a53364adf5869ddafbf45b94be599f1c2c747622b083477a614`.
- `intra8.mp4` SHA-256: `79997129ee3ad3321af270c280d3547222ed7109b66431dedd56912c6bb0a0ed`.
- `intra8.yuv.gz` SHA-256: `3da6fdad94f7823315dbc8befadd05863793619aed3cc67cf3c0b5da8fdd7d0c`.

The four 64x48 synthetic HEVC videos contain twelve frames with B-picture reordering, in Main 8-bit and Main10 4:2:0. MP4 and Matroska retain the same encoded samples. They contain no external artwork or recordings.

Generation uses the independent x265 encoder through FFmpeg with `testsrc2=size=64x48:rate=12:duration=1`, the matching `yuv420p` or `yuv420p10le` pixel format, and `pools=none:frame-threads=1:wpp=0:keyint=12:min-keyint=12:bframes=2:repeat-headers=0`. MP4 uses the `hvc1` sample entry and a front-loaded movie index; Matroska is a stream copy of that file.

Each JSON reference is independent `ffprobe -show_streams -show_packets -show_data_hash sha256` output. Tests compare original compressed packet hashes and presentation timestamps, including reverse requests. MP4 decode timestamps are also compared; Matroska does not declare decode timestamps.

`weighted8.mp4` and `weighted10.mp4` contain 24 synthetic 192x128 pictures, three B pictures, multiple references and explicit luma/chroma weights. Generation adds `fade=t=in:st=0:d=2`, uses two-second duration, `bframes=3:weightp=1:weightb=1`; Main uses `pools=1:wpp=1`, Main10 uses `pools=none:wpp=0`. Other encoder options match the twelve-picture samples.

The four `.headers.json` files retain independently parsed SPS/PPS and slice fields from FFmpeg `trace_headers`, with each slice's final bit position after alignment. Seventy-two headers verify I/P/B syntax, reference counts, temporal prediction, Main/Main10 weight derivation and exact entropy offsets. The trace parser names the coded chroma offset delta `chroma_offset_l0/l1`.

The four 64x48 synthetic HEVC videos contain twelve frames with B-picture reordering, in Main 8-bit and Main10 4:2:0. MP4 and Matroska retain the same encoded samples. They contain no external artwork or recordings.

Generation uses the independent x265 encoder through FFmpeg with `testsrc2=size=64x48:rate=12:duration=1`, the matching `yuv420p` or `yuv420p10le` pixel format, and `pools=none:frame-threads=1:wpp=0:keyint=12:min-keyint=12:bframes=2:repeat-headers=0`. MP4 uses the `hvc1` sample entry and a front-loaded movie index; Matroska is a stream copy of that file.

Each JSON reference is independent `ffprobe -show_streams -show_packets -show_data_hash sha256` output. Tests compare original compressed packet hashes and presentation timestamps, including reverse requests. MP4 decode timestamps are also compared; Matroska does not declare decode timestamps.

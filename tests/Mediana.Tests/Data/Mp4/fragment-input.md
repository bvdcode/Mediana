# Fragmented MP4 input fixtures

These files remux the existing `avc-aac.mp4` fixture without changing compressed audio or video. FFmpeg 6.1.1 generated them with `-map 0 -c copy -frag_duration 200000` and these `-movflags` values:

| File | Flags |
| --- | --- |
| fragment-relative.mp4 | empty_moov+default_base_moof |
| fragment-absolute.mp4 | empty_moov |
| fragment-implicit.mp4 | empty_moov+omit_tfhd_offset |
| fragment-signed.mp4 | delay_moov+default_base_moof+negative_cts_offsets |
| fragment-hybrid.mp4 | frag_keyframe |

Each `.json` contains the unmodified `ffprobe -ignore_editlist 1 -show_packets -show_streams -show_data_hash sha256 -of json` result plus the file's SHA-256. Pixel references are shared with `avc-aac.sequence.yuv`.

The tests distinguish raw container timing from FFprobe's packet interpretation:

- The first three fixtures declare duration 19,200 for the first AAC sample in `tfhd`; FFprobe reports the AAC frame length of 1,024. Subsequent samples have duration 1,024.
- The signed fixture declares a minimum composition offset of -2,048. FFmpeg 6.1.1 adds 2,048 to packet PTS (`dts_shift` in its MOV demultiplexer). Raw media timestamps retain `tfdt + trun` values; the comparison explicitly removes the reference's PTS translation. The first AAC duration is 1,024.
- The signed fixture has zero-duration media edits, which ISO/IEC 14496-12 clause 8.6.6 defines as offsets continuing through subsequent movie fragments. These are not empty presentations.

All packet payload hashes, raw DTS, normalized PTS and declared durations are checked. Complete video decode compares every pixel, followed by backward indexed seeks. Opening is checked for absence of reads overlapping any packet payload.

`fragment-aac.m4a` and `fragment-pns.m4a` remux the existing `Data/Aac/lc-mono-48.m4a` and `lc-pns-48.m4a` with `empty_moov+default_base_moof` and 100,000-microsecond fragments. They reuse the existing independent Float32 references. Every restart checks the original timestamps; tonal PCM is byte-identical to sequential decode, while noise-substitution checks use the established per-channel-block energy tolerance of 6 percent.

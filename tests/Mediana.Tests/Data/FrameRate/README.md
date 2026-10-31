# Indexed video-rate references

These original generated 96x80 AVC/AAC files exercise fractional constant timing,
variable timing, B-picture reordering and a copied edit interval. The reference
records exact FFmpeg/ffprobe 6.1.1 commands and input/configuration/packet hashes.
No external media tool runs in Mediana or its tests.

| File | Coded pictures | Exact average pictures/s |
| --- | --- | --- |
| `frame-rate-cfr.mp4` | 30 | 30000/1001 |
| `frame-rate-vfr.mp4` | 30 | 900000/32213 |
| `frame-rate-edited.mp4` | 23 | 2070000/84257 |

Each average is independently calculated as the complete video sample count times
the media time scale divided by the sum of `stts` sample durations. FFprobe's
`avg_frame_rate` agrees exactly, and its independently decoded picture count
agrees with the indexed count. The CFR sample duration is 3003 ticks at 90000 Hz;
the VFR file contains 1800-, 3003- and 4500-tick durations. Their averages do not
assert a constant presentation cadence or a count after applying edit lists.

Packet digests cover every original PTS, DTS, indexed duration, length and payload.
FFprobe supplies packet offsets, bytes and timestamps; a separate Python parser
reads all durations directly from `stts`, including VFR packet durations omitted
by ffprobe. Movie-clock track duration declarations remain separate reference
fields. Audio access units do not become video frames or receive a frame rate.

Managed and Linux Native AOT checks verify both timestamp modes, the public probe
and native track APIs, exact fractions, all packet/configuration hashes, retained
metadata, source cursors and released media storage. CLI checks cover fractional
formatting and omit unknown rates.

The same public video sessions decode every picture in each file. Every output
PTS, presentation order, dimensions and the complete YUV420 byte hash match
independent decoding, including B-picture reordering and VFR timing. These
references use the original media timeline with edit-list processing disabled.

Format references: [sample timing](https://developer.apple.com/documentation/quicktime-file-format/time-to-sample_atom),
[constant video timing](https://developer.apple.com/documentation/quicktime-file-format/creating_video_tracks_at_30_frames_per_second),
[variable video timing](https://developer.apple.com/documentation/quicktime-file-format/video_variable_frame_rate_indication).

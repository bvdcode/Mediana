The public decode, preparation, H.264/AAC encoding and HLS APIs are checked as
one complete operation. Sources include AV1 with HE-AAC v2 parametric stereo,
AVC with AC-3, and AVC with E-AC-3 transient processing. Segments requested out of
order are identical when requested again, and all owned media storage is
released after disposal. Missing chroma positioning in the AV1 test input is
supplied explicitly through `VideoPreparationOptions.SourceColorInterpretation`.

FFmpeg 9.0.2 independently decodes the complete two-second playlists and seeks
to 0.5, 1.3 and 1.8 seconds without errors. Source-to-output audio correlation
windows select zero sample displacement. The E-AC-3 source reference includes
independently calculated transient reconstruction under ETSI TS 102 366,
clause E.2.7; it is not described as an unmodified FFmpeg transient reference.

A longer source contains 120 seconds of 96×64 AVC at 24 fps and continuous
48 kHz stereo AC-3. The common HLS session produces thirty four-second windows,
encoding AVC and 64 kbit/s AAC. The first requested window is 116–120 seconds,
followed by the first, middle, earlier and repeated final windows, then all
remaining windows.

| Complete-operation check | Result |
| --- | --- |
| Source bytes | 4,464,273 |
| Bytes read through opening and the first, final-window request | 228,907 |
| Bytes read during opening alone | 81,918 |
| Independent full playback | All 2,880 video frames, no decoder errors |
| Independent input seeks | 0.5, 31.7, 60.1, 116.3 and 119.2 seconds, no decoder errors |
| Repeated final segment | Identical bytes |
| Audio correlation windows | Best displacement is zero at 0, 0.5, 30, 60, 90, 116 and 119.5 seconds |
| Owned media bytes after disposal | 0 |

The source contains 5,759,744 decoded audio samples per channel after its MP4
edit. The output contains 5,760,000, leaving a 256-sample tail, approximately
5.3 ms, at the end. No accumulated audio displacement was observed over this
two-minute operation. These are correctness and bounded-read checks; the
independent playback executable is not a CPU performance baseline.

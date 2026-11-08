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

An additional ordinary libaom AV1 source contains 288 visible Main-profile
8-bit 160×136 pictures at 24 fps and continuous 48 kHz stereo AC-3 over twelve
seconds. It uses default reconstruction tools, a four-second keyframe interval
and explicitly declared BT.709 limited-range color with vertical chroma
position. No source-color replacement is configured by the client.

The public MP4 reader and AV1 decoder return all 288 pictures with matching
rational presentation timestamps and durations. Full-plane comparison against
dav1d finds every visible sample byte-identical after accounting for plane
strides. The comparison includes decoded hidden pictures used for prediction.
The common HLS session produces six two-second H.264/AAC windows. Requests run
in the order 5, 0, 2, 1, 5, 3, 4; both requests for the final window produce
identical bytes. Opening reads 9,083 bytes, and opening plus the first request
reads 106,210 bytes of the 439,577-byte source.

Independent HLS playback decodes all 288 pictures from both files and HTTP.
Input seeks to 0.5, 3.7, 6.1, 10.3 and 11.5 seconds pass on both transports.
Audio comparison in six windows spanning 0.25–11.75 seconds finds zero sample
displacement, with correlation above 0.99999987. The decoded output has a
768-sample tail relative to the edited source, approximately 16 ms; this is
an end padding difference, not measured accumulated drift.

The complete consumer also builds with strict Native AOT checks. Its playlist,
initialization and seven media outputs are byte-identical to the JIT run, and
the media allocator retains zero bytes after disposal. This ordinary source
does not establish support for every AV1 reconstruction tool or bit depth.

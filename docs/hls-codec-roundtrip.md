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

Continuous HE-AAC and HE-AAC v2 sources extend audio coverage to sixty seconds.
Unmodified FDK-AAC 2.0.3 encodes a changing stereo chirp/transient signal in one
encoder instance per source, using AOT 5 at 48 kbit/s and AOT 29 at 32 kbit/s.
Each MP4 combines that audio with 720 AVC pictures at 160×96 and 12 fps. Complete
source PCM has the same sample count as FFmpeg 6.1.1; floating-point comparison
measures 92.76 dB SNR for HE-AAC and 70.20 dB for HE-AAC v2.

The public HLS session produces fifteen four-second H.264/AAC segments, starting
with requests 14, 0, 7, 1, 14 and 7. Repeated segments are byte-identical. Opening
and the first final-window request read 90,718 bytes of the 1,036,358-byte HE-AAC
source and 80,719 bytes of the 916,474-byte HE-AAC v2 source. Owned media storage
returns to zero after disposal.

FFmpeg 9.0.2 passes all 24 complete-playback and seek checks over files and HTTP.
The seeks cover 0.5, 15.37, 31.1, 44.9 and 59.5 seconds. Sixteen audio comparison
windows spanning the beginning, every segment boundary and the tail select zero
sample displacement; the lowest correlations are 0.99999187 and 0.99957467.
HTTP seek-content checks use absolute presentation timestamps with
[`-seek_timestamp 1`](https://ffmpeg.org/ffmpeg.html#Advanced-options): default
`-ss` is relative to the file start, which includes negative AAC priming time.

Video covers 0–60 seconds. AAC packets are contiguous on a 1/48000 clock, starting
at −1024 with a 1024-sample skip indication. Decoded output contains 2,880,512
samples per channel, including a 512-sample encoded tail beyond sixty seconds
(10.67 ms). The source encoder tails extend beyond the selected video interval.
Separate initialization-plus-fragment decoding starts audio at 28.032 and
56.021333 seconds for windows starting at 28 and 56 seconds. Its decoded samples
match their declared presentation times; these checks do not establish exact
sample coverage at the beginning of an isolated audio fragment.

A Main10 MP4 source adds twelve 160×136 AV1 pictures at 6 fps with continuous
48 kHz stereo AC-3. The public client explicitly selects `Yuv420P8` frame
preparation and `VideoColorPolicy.ResolveForEncoding`. Rounded conversion from
ten to eight bits emits a warning; declared BT.709 limited-range color and left
chroma positioning are preserved.

The session creates four half-second H.264/AAC windows and serves requests
3, 0, 2, 1, 3 and 2. Both repeated segments are byte-identical. Opening reads
1,528 bytes; format discovery and preparation raise this to 6,608 bytes. Through
the first request, for 1.5–2 seconds, counted reads total 49,541 bytes of the
77,485-byte input. This source has only an initial random-access picture, so
the late request reconstructs the preceding video pictures needed as references.

FFmpeg 9.0.2 passes sixteen full-playback and absolute-seek checks over files
and HTTP. Seeks cover 0.25, 0.5, 0.75, 1, 1.25, 1.5 and 1.75 seconds. Complete
video timestamps are 0 through 11 on a 1/6 clock; each initialization-plus-fragment
pair independently decodes three pictures, beginning with a key picture at the
corresponding window start. Five audio windows spanning the beginning, all three
segment boundaries and the tail select zero displacement, with correlation at
least 0.99998336. AAC timestamps are contiguous on a 1/48000 clock, with a
1,024-sample priming skip. Decoded output contains 96,256 samples per channel,
including a 256-sample tail beyond the two-second video interval; the source
declares audio through 1.994 seconds.

The same public consumer builds and runs with strict Native AOT checks. All
eight playlist, initialization and media files match the JIT output byte for
byte. Owned media storage returns to zero after both executions.

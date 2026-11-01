Mediana provides explicit mono and stereo channel mixing independently of any
container or codec. `AudioDownmix` constructs an `AudioChannelMixer`; callers can
also supply their own matrix directly.

```csharp
AudioChannelMixer mixer = AudioDownmix.CreateStereo(input.Format, AudioSampleFormat.Float32);
using AudioFrame stereo = mixer.Apply(input, allocator, cancellationToken);
```

Use `Mediana.Processing.Audio` and `Mediana.Core.Audio`. The factory returns
Float64 by default; Float32 writes the encoder's input representation directly
without an intermediate PCM frame. Both use double-precision accumulation.
Mixing borrows its input and returns independently owned, interleaved PCM with
the original sample rate, sample count and timestamps. Integer output requires
an explicit subsequent sample conversion.

## Speaker policy

The mono and stereo coefficients follow
[ITU-R BS.775-4, Annex 4, Table 2](https://www.itu.int/dms_pubrec/itu-r/rec/bs/R-REC-BS.775-4-202212-I!!PDF-E.pdf).
Front left/right, center and left/right surround speakers have named
contributions. Back and side surround labels are alternatives for the same role;
they cannot coexist in one standard matrix. The LFE channel is omitted.
Unknown speakers, additional surrounds and wide front speakers require a caller's
explicit `AudioChannelMixer` matrix.

A common divisor, at least one, is the largest absolute coefficient sum of any
output row. All coefficients are divided by that value. This bounds output for
unit-bounded input while preserving stereo balance, including asymmetric layouts.
It is matrix attenuation, without a limiter or loudness normalization. Finite
input headroom remains possible.

For 5.1 stereo, each output has front gain `1/(1+sqrt(2))`, center gain
`(1/sqrt(2))/(1+sqrt(2))`, and the same gain for its corresponding surround.
Mono uses front gains `1/sqrt(2)`, center gain `1`, and surround gains `0.5`,
then divides every coefficient by their sum. Single-channel center input remains
unchanged in mono; creating stereo places it in both outputs at -3 dB.

## Console encoding

```sh
mediana -i input.m4a output.ts --audio-codec Aac --audio-channels 2
mediana hls encode input.mp4 output-hls --audio-stream 1 --audio-codec Aac --audio-channels 2 --widths 640 320
```

`--audio-channels 1` selects mono; `2` selects stereo. Both commands require
explicit AAC encoding. Omitting the option preserves the source channel order;
a source with more than two channels then requires an explicit downmix before
it can enter the mono/stereo AAC encoder. Copying streams and video-only HLS do
not accept this option. HLS applies source movie edits before mixing; standalone
audio encoding retains the complete decoded source timeline. New encoder priming
and padding remain part of the MPEG-TS output.

## Verification

Managed and Native AOT checks cover every shuffled 5.1 speaker, back/side aliases,
correlated peaks, asymmetric stereo balance, both PCM storage layouts, Float32
and Float64 output, exact timing and released media storage.

The complete original film's AAC 5.1 track was independently decoded with FAAD2,
then mixed with FFmpeg's explicit normalized stereo pan matrix. The public Mediana
pipeline matched all 57,262,080 output Float32 samples within
`3.5762786865234375e-7`, preserved every source timestamp and duration, and
released all media storage. Neither verification tool is a product dependency.

The short audiovisual HLS reference produces eight pictures, fifteen AAC packets
and three aligned segments in each of two variants. Managed and Native AOT files
match byte for byte. Existing stereo AAC transport output also remains
byte-identical to the preceding implementation when channel selection is omitted.

## Complete audio encoding

The complete 596.48-second coded audio track also passes through the Native AOT
console path: AAC 5.1 decoding, normalized stereo mixing and AAC-LC encoding
with a 128000-bit/s ceiling. All 28,631,040 input samples per channel produce
27,961 AAC access units, with 1024 new priming samples and no padding.
Every packet's PTS, DTS, duration and byte-aligned bit ceiling were checked.
The raw AAC average is 125681.54 bit/s; MPEG-TS occupies 12,616,116 bytes.

Independent FFmpeg decoding and Mediana decoding of the newly encoded stream
match on all 57,264,128 Float32 values within `2.384185791015625e-7`.
After removing exactly the declared new priming interval, full-track SNR against
the independent stereo input is 25.41 dB left and 25.74 dB right. These measure
signal error; perceptual and listening acceptance remain open.

One isolated complete-file Linux x64 Native AOT run on logical CPU 2 used
87.146 seconds of CPU, 88.161 seconds elapsed and 28.1 MiB peak RSS. This covers
decoding, mixing, encoding and transport output, with zero live media buffers
at EOF. It is a resource observation, without an equal-quality encoder comparison.

## Complete audiovisual HLS encoding

The same original 1280x720, 24-frame/s MOV was processed to completion with
stereo AAC at 128000 bit/s and two AVC variants: 320x180 at 384000 bit/s and
160x90 at 96000 bit/s, each with a two-second packet-rate buffer.
All 14,315 source pictures produce 100 aligned six-second segments per variant,
with a shorter final segment. The complete VOD duration is 596.48 seconds;
the master, two media playlists and 200 transport segments total 203 files.

Independent playlist decoding verifies every picture and its exact presentation
time and duration. Native AOT decoding after lossless MP4 remux and parameter-set
canonicalization matches all 1,546,020,000 visible YUV bytes and every remuxed
picture timestamp. Every coded VCL NAL remains byte-identical through that
verification remux. Continuous decoding reaches EOF without retained media storage.
The largest consecutive-window packet-rate excess is 767576 of 768000 permitted
bits for the larger variant and 191784 of 192000 bits for the smaller one.
The resulting video packet averages are 384823.15 and 96214.64 bit/s; the initial
buffer credit permits a finite-file average above the configured replenishment rate.

Independent MOV edit-list inspection selects exactly 28,630,160 PCM samples per
channel. Both variants contain identical AAC payloads and timestamps, with
27,961 packets, 1024 priming samples and 880 padding samples. Every audio packet's
PTS, DTS, duration and raw bit ceiling pass. Independent and Native AOT decoding
of each complete audio track match all 57,264,128 Float32 values within
`2.384185791015625e-7`. Their media storage is fully released at EOF.

One isolated Linux x64 Native AOT run on logical CPU 2 used 761.113 seconds of CPU,
809.414 seconds elapsed and 97.8 MiB peak RSS. It includes source video/audio
decoding, both video resizes and encoders, one shared stereo AAC encode and all
HLS output. This verifies complete processing of an HD source to these two output
sizes. Full-HD output encoding, equal-quality performance comparisons and
perceptual quality acceptance remain open.

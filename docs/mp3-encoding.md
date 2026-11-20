# Layer III encoding and MP3 files

`Mediana.Codecs.Mp3` encodes interleaved Float32 mono/stereo PCM into complete
MPEG-1, MPEG-2 and MPEG-2.5 Layer III frames. The supported sample rates are
48,000, 44,100, 32,000, 24,000, 22,050, 16,000, 12,000, 11,025 and 8,000 Hz.
Bitrates use the standard table for the selected MPEG version. No native codec
or external process is used.

```text
mediana -i video.mp4 audio.mp3
mediana -i recording.avi audio.mp3 --audio-bit-rate 192000
mediana -i recording.wav audio.mp3 --audio-channels 1 --overwrite
```

The input container selects its registered decoder. An explicitly selected mono
or stereo output uses the shared normalized downmix. Other sample rates require
an explicit resampling stage; the encoder does not silently resample input.

## Library pipeline

Register `Mp3EncoderFactory` and `Mp3WriterFactory` alongside the desired input
readers and audio decoders, then call:

```csharp
AudioTranscodeResult result = await engine.TranscodeAudioAsync(
    source, destination, "mp3", "mp3", allocator,
    new AudioTranscodeOptions { StreamIndex = audioStreamIndex },
    cancellationToken: cancellationToken);
```

`PreparedAudioSource` opens the registered input once, applies its optional
`IContainerPresentationReader` selection and prepares Float32 PCM. File encoding
and shared HLS audio use this same source. MP4 readers should use
`Mp4TimestampMode.LinearMovie` when movie edits select the output timeline.
The CLI registers that mode for audio conversion.

For packet-level use, `Mp3Encoder.Create` accepts a format, exact sample clock,
allocator and options. Input timestamps must be known, sample-exact and
contiguous. At most one input frame lease is retained. The workspace and output
packets use the supplied allocation budget; no full recording is buffered.
Reset discards pending input and filter state. Failure or cancellation requires
reset or disposal. Returned packets own their storage and may outlive the encoder.

The filterbank reuses the normative synthesis coefficients with the ISO analysis
normalization. Hybrid MDCT, alias reduction, power-law quantization and Huffman
coding run in managed code. SIMD dot products implement the matrix operations;
Huffman selection counts symbol frequencies once per region. The current encoder
uses long blocks, independent left/right channels and no bit reservoir. Short-block
switching, perceptual noise shaping and joint-stereo encoding remain open quality
work; successful decoding alone does not establish equal perceptual quality.

## Timing and file finalization

`IAudioEncoderTiming` reports 1,057 decoded priming samples and the final padding.
`AudioPacketTrim` side data declares stream-edge trimming in samples per channel;
packet durations continue to describe the complete coded frames. End trimming
becomes known when the encoder drains. No input samples are dropped to complete
an output frame.

`Mp3Writer` writes one MP3 stream to a seekable destination. It reserves bounded
metadata space, writes audio incrementally and finalizes Xing statistics and an
ID3v2.4 `iTunSMPB` tag. Statistics that exceed their 32-bit Xing fields are omitted;
64-bit audible sample counts remain in the ID3 tag. Known packet timestamps must
be sample-exact and contiguous because an elementary MP3 file cannot preserve
gaps or overlaps. Disposal aborts unfinished output and leaves the destination
open; only `CompleteAsync` finalizes the file.

`Mp3Reader.GetPresentationRange` exposes a valid explicit `iTunSMPB` selection.
Raw packet and decoder APIs preserve all coded samples. `PreparedAudioSource`
applies the selection. Xing counts are checked against the declared trimming;
malformed text is retained as ordinary metadata. No encoder identity or missing
delay is guessed.

Players differ in their support for MP3 gapless tags. FFmpeg 6.1 and 9.0.2 decode
the generated audio but do not apply `iTunSMPB` to MP3. PCM comparison with those
versions explicitly excludes the declared priming and padding. The file does not
claim a different encoder identity to trigger player-specific tag handling.

## Verification and measurements

Managed and Native AOT checks cover all nine sample rates, mono/stereo,
frame syntax, decoded signal quality, exact coded sample accounting, reset and
buffer ownership. File tests exercise AVI/MP4 decoding, MP3 encoding, metadata
reading and selected PCM decoding through registered factories.

A 30-second 48 kHz stereo mixture of tones, filtered noise and periodic transients
was encoded at 192 kbit/s. These are whole-process measurements, including input
reading, startup and file finalization; they are not a perceptual listening test.

| Implementation | Elapsed | CPU | Peak RSS | File size | Decoded SNR |
| --- | ---: | ---: | ---: | ---: | ---: |
| Mediana, before histogram selection | 1.153 s | 1.395 s | 48.98 MiB | 721,216 B | 27.03 dB |
| Mediana, histogram selection, median of three runs | 0.801 s | 0.899 s | 44.50 MiB | 721,216 B | 27.03 dB |
| FFmpeg 6.1.1/libmp3lame, one thread | 0.357 s | 0.374 s | 51.96 MiB | 721,197 B | 25.95 dB |

The optimized MP3 file was byte-identical to the preceding implementation.
The SNR comparison uses independently decoded, time-aligned PCM. The similar
file sizes and this signal's SNR do not establish general quality parity with
libmp3lame. Mediana processes this input faster than real time; libmp3lame remains
faster in both elapsed and CPU time.

# AAC-LC references

The WAV inputs are original generated signals: distinct channel tones at 431,
1379 and 3613 Hz, deterministic low-level noise and short periodic impulses.
They contain 0.37 seconds of signed 16-bit PCM. The included sources allow
regeneration of the AAC files without third-party media.

`references.json` records the encoder version, complete encoding and decoding
commands, source/input/reference SHA-256 hashes and independent packet/stream
metadata from ffprobe. FFmpeg is used only to create and validate test fixtures;
the library and test executables do not invoke it.

| Fixture | Rate | Channels | PNS | Frames | Decoded samples per channel |
| --- | --- | --- | --- | --- | --- |
| `lc-mono-48` | 48000 | Mono | Disabled | 19 | 19456 |
| `lc-stereo-44` | 44100 | Stereo | Disabled | 17 | 17408 |
| `lc-pns-48` | 48000 | Stereo | Enabled | 19 | 19456 |

Each fixture uses all four window sequences and both sine/KBD shapes. The mono,
stereo and PNS files contain respectively 2, 2 and 5 TNS filters. Stereo fixtures
exercise 374 and 292 selected M/S bands; both contain intensity-stereo bands.
Together the files exercise all eleven spectral codebooks. These syntax counts
are checked alongside the complete PCM or energy comparisons.

The `.f32le` references contain complete AAC frames, including encoder priming
and final padding. Reference decoding uses `-ignore_editlist 1`, matching the
MP4 reader's media timeline. These sample counts intentionally exceed the source
WAV lengths. Frame duration is 1024 samples even when the container declares a
shorter final packet duration. No delay or padding trimming is claimed.

Without PNS, every interleaved PCM sample must match the independent decoder
within an absolute error of `2e-6`, including short/long window transitions and
the beginning and end of the stream. With PNS, random sample sequences can differ
between decoders. Each channel's 1024-sample block energy must agree within 6%
and the complete stream energy within 0.1%; separate spectral tests verify exact
noise-band normalization and stereo correlation. This is an energy comparison,
not a claim of sample equality for PNS.

Managed and Native AOT checks use the same complete-sequence verifier. They
check hashes, format, channel order, every presentation timestamp and duration,
sample count, EOF, a 128 KiB media-buffer budget, unchanged borrowed-source
position, retained-frame lifetime and final buffer release.

Two additional policies are checked separately on the mono/stereo files without
PNS. Removing only the first 1024 samples matches ordinary FFmpeg decoding,
including the final frame padding (18432 and 16384 samples per channel).
Selecting the exact edit interval removes both ends and returns 17760 and 16317
samples, matching FFmpeg followed by an explicit `atrim=end_sample=...` filter.
`references.json` records both commands, complete-output hashes and sample counts.
The stored full-frame reference supplies both verified PCM slices without adding
duplicate raw files. Shared managed/Native AOT checks exercise the actual MP4
range mapper and streaming trimmer, exact output PTS/durations and independent
output ownership after disposing the trimmer and audio session.

AAC numeric codebooks, scalefactor bands and window/TNS equations come from
ISO/IEC 13818-7:2004 and ISO/IEC 14496-3:2009. The decoder implementation is
original C# code. The all-ones M/S mask with intensity-stereo books is rejected,
as required by the AAC conformance rules; encoder tests use automatic M/S
selection (`-aac_ms -1`).

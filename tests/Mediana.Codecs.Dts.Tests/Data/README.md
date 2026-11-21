# DTS reference audio

`stereo`, `surround`, `mono44100` and `noise` are complete short Matroska tracks
encoded with the independent FFmpeg 6.1.1 DTS encoder (`-c:a dca -strict -2`).
Sources are generated tones or deterministic noise, with no third-party recordings.

| File | Source | Encoding |
| --- | --- | --- |
| stereo | 431/719 Hz, 48 kHz stereo | 768 kbit/s |
| surround | Distinct 5.1 tones with a 47 Hz LFE | 1536 kbit/s, ADPCM enabled |
| mono44100 | 939 Hz, 44.1 kHz mono | 384 kbit/s |
| noise | White noise, seed 42, amplitude 0.15, stereo | 384 kbit/s, ADPCM enabled |

`vectors` and `predictors` use synthetic Core packets exercising 64 spread-out
codebook entries each, including both ends of the index ranges. Each packet is
followed by a zero packet to observe filter decay. Reconstruction filters alternate
between perfect and non-perfect. The elementary streams are also included.

PCM references are interleaved float32 little-endian in DTS coded channel order:

    ffmpeg -flags +bitexact -channel_order coded -i input.mkv -c:a pcm_f32le -f f32le output.pcm

The surround reference omits `-flags +bitexact`: FFmpeg's floating LFE path uses
the published FIR interpolator, while its fixed path uses a different LFE filter.
Conversely, its floating perfect-reconstruction main filter has older coefficients;
its fixed main filter matches the published ETSI coefficients. These differences
are why reference paths are selected explicitly. Full-sequence checks use an
absolute float tolerance of 0.00002, including independent rounding differences.
Reverse seeks must reproduce the decoder's sequential PCM exactly.

The normative scale, quantizer, Huffman and FIR constants come from ETSI annex D.
Annex D.10 describes but does not print the large predictor and VQ tables. Those
numeric vectors were reconstructed experimentally from independent decoder output:
known single-subband impulses define the synthesis operator; least-squares inversion
recovers subband responses; fourth-order recurrence fitting recovers predictor
coefficients in units of 1/8192; VQ responses recover signed integer vectors.
All 4096 predictor entries and 1024 vectors were recovered and checked against
integer quantization. Four missing D65 Huffman symbols and a duplicated sign in
D129 were resolved with independently decoded symbol packets. No external decoder
implementation is embedded in the library.

Direct packet tests cover all four word packings, backpressure, retained output,
truncation, cancellation, reset and termination padding. The four packed forms of
the complete stereo stream were also independently decoded to identical PCM.
Matroska tests cover prefix-only metadata, lacing, final-frame trimming and rejected
track/header contradictions.

An extended full-codebook check decoded all 1024 VQ indexes (2,097,152 PCM samples)
and all 4096 predictor indexes (8,388,608 PCM samples), including each filter's
decay. Against independent fixed-point reconstruction, maximum absolute errors
were 0.00000458 and 0.00000315 respectively; RMS errors were below 0.000000284.

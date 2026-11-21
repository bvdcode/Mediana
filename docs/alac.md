# ALAC decoding

Mediana.Codecs.Alac supplies AlacDecoderFactory for complete Apple Lossless access
units. Register it with Mp4ReaderFactory to decode M4A/MP4 audio through the common
AudioSession API, including indexed seeks. Each packet reconstructs independently;
seeking does not require decoding the preceding audio.

Output is native-endian interleaved signed32 PCM with source precision aligned to
the most significant bits. The decoder supports 16/20/24/32-bit samples,
uncompressed elements, adaptive prediction, Rice residuals, zero runs, stereo
mixing, low-byte restoration and up to eight channels. Returned audio frames retain
their storage independently of the session. Temporary sample buffers count against
the caller's allocator budget.

The configuration preserves the optional ALAC channel-layout record. Mono and
stereo have their defined layouts. More than two channels without a layout record
remain discrete channels in encoded order, as specified by Apple's cookie format;
no speaker placement is inferred from channel count alone.

Six independent FFmpeg encodings cover mono, stereo, six channels, 16/24-bit audio,
silence and noise. Complete PCM and reverse packet seeks match independently
decoded samples exactly. Packet tests exercise full-range 16/20/24/32-bit raw
samples, retained output, backpressure, truncation, cancellation and reset.

Format references: [Apple ALAC](https://macosforge.github.io/alac/),
[ALAC magic-cookie description](https://github.com/macosforge/alac/blob/master/ALACMagicCookieDescription.txt).

A Native AOT full decode-and-SHA-256 measurement on 60-second, 48 kHz stereo files
used about 17 MB peak process RSS. Across three fresh processes, median CPU was
0.290 s for tones and 0.216 s for deterministic noise; FFmpeg 6.1.1 used 0.290 s
and 0.210 s respectively, with about 54 MB peak RSS. Median elapsed times were
0.240/0.194 s for Mediana and 0.187/0.136 s for FFmpeg. Both measurements include
process startup and complete PCM hashing. Mediana used pooled buffers; FFmpeg
used one configured decoder, output and filter thread. These short generated
workloads do not establish performance across arbitrary music recordings.

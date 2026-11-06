The shared AAC allocator compares stereo reconstruction error relative to each
channel's complete spectral energy. It uses the existing per-band search, exact
syntax costs, packet budget and automatic window decisions.

For each channel, the objective is the sum of band squared errors divided by that
channel's spectral energy for the current access unit. Both channels contribute
to the shared objective. Mono retains its previous arithmetic. Digital silence
has zero reconstruction error and requires no division. The initial uniform-scale
seed, candidate limits and 32/64-move bounds are unchanged. At most two doubles
are added on the stack; pooled and heap working storage remain unchanged.

This is an encoder allocation choice. It does not apply auditory masking,
frequency-dependent thresholds or a guaranteed channel quality floor. AAC syntax,
scale reconstruction, priming, padding and output clocks retain their existing
contracts.

## Complete signal comparisons

Each source was encoded before and after normalization at the same raw AAC
ceiling and independently decoded in full. SNR removes exactly the encoder's
1024 priming samples and final padding. Values below retain the individual
channels; `silent` denotes an unchanged zero source and zero decoded channel.

| Source | Ceiling, kbit/s | Previous channel SNR, dB | Normalized channel SNR, dB | Previous/normalized raw kbit/s |
| --- | --- | --- | --- | --- |
| Mono tone, 48 kHz | 64 | 19.14 | 19.14 | 61.88 / 61.88 |
| Stereo tones, 44.1 kHz | 128 | 19.71 / 19.33 | 19.55 / 19.53 | 125.55 / 125.51 |
| Stereo noise, 48 kHz | 192 | 23.45 / 22.85 | 23.32 / 22.99 | 189.43 / 189.51 |
| Silent right channel | 96 | 60.07 / silent | 60.07 / silent | 94.13 / 94.13 |
| Right at 5% amplitude | 96 | 52.48 / 31.66 | 46.76 / 37.37 | 94.13 / 94.12 |
| Alternating active channels | 96 | 40.30 / 38.34 | 40.13 / 37.98 | 94.17 / 94.17 |
| Generated music | 128 | 74.66 / 75.50 | 74.84 / 75.34 | 122.31 / 122.31 |
| Recorded mono speech | 32 | 24.19 | 24.19 | 31.25 / 31.25 |
| Impulses | 96 | 29.61 / 27.81 | 29.24 / 28.05 | 7.44 / 7.44 |

The quiet channel improves by 5.71 dB on the unequal-amplitude source, while the
loud channel loses 5.72 dB. Aggregate source SNR drops from 51.35 to 46.68 dB;
allocation deliberately changes the balance of relative error. Mono tone,
recorded mono speech and the silent-channel source retain byte-identical output.
Other channels include small improvements and regressions. These measurements
do not establish a general perceptual improvement.

## Complete recorded music

The five complete [EBU SQAM recordings](https://tech.ebu.ch/docs/tech/tech3253.pdf)
cover castanets, harpsichord, piano, wind ensemble and pop. Each version encodes
216 seconds and 9,311 access units at the same 128 kbit/s raw ceiling. Every
access unit remains within the byte-aligned ceiling.

| SQAM source | Previous left/right SNR, dB | Normalized left/right SNR, dB | Previous/normalized raw kbit/s |
| --- | --- | --- | --- |
| Castanets | 20.85 / 20.82 | 20.82 / 20.83 | 99.69 / 99.71 |
| Harpsichord | 19.97 / 19.93 | 19.84 / 20.02 | 103.05 / 103.09 |
| Piano | 41.23 / 42.29 | 41.48 / 42.02 | 120.06 / 120.14 |
| Wind ensemble | 23.54 / 24.04 | 23.70 / 23.83 | 98.87 / 98.89 |
| Pop | 24.96 / 24.80 | 24.99 / 24.74 | 108.24 / 108.27 |

Channel SNR changes range from -0.27 to +0.25 dB. Native AOT and managed encoding
produce identical complete transport bytes for all five recordings and the nine
signals above. Native AOT decoding of all 19,068,928 SQAM Float32 values agrees
with independent decoding within `1.7881393432617188e-7`; all decoded values are
finite and media storage is released at EOF.

## Regression coverage

Two complete unequal-channel tests exchange left/right roles. They check all
108,037 input samples per channel, 107 access units, exact PTS/DTS/durations,
1024 priming samples, 507 padding samples, finite decoded PCM and released storage.
On this seven-tone source, each orientation must retain at least 45 dB in the loud
channel and 36 dB in the channel at 5% amplitude. The preceding allocator fails
with approximately 31.67 dB in the quiet channel. Both checks run in managed and
Native AOT builds.

Four complete public encoding references and controlled mono/stereo window
references retain their existing independent-decoding tolerances. A separate
complete mono/stereo matrix covers all twelve sample rates: 336 access units,
including attack and final-drain handling. The short 5.1-to-stereo HLS case retains
identical managed/Native AOT files, eight pictures, fifteen audio packets and three
aligned segments in each variant.

## Complete ten-minute resource comparison

The source contains 26,460,000 samples per channel at 44.1 kHz, stereo, encoded
at the same 128 kbit/s raw ceiling. Linux x64 Native AOT processes use one logical
CPU, separate short warmups and sequential before/after/after/before order.
No other build, test or media workload runs during measurement. Each version has
two complete measured runs; the table gives medians of CPU time, elapsed time and
process peak RSS.

| Allocator | CPU, seconds | Elapsed, seconds | Peak RSS, MiB |
| --- | --- | --- | --- |
| Absolute channel distortion | 80.38 | 81.56 | 27.19 |
| Relative channel distortion | 86.11 | 87.30 | 27.44 |

CPU increases by 7.14% in this comparison. Individual CPU measurements are
80.372–80.383 seconds before and 85.555–86.675 seconds after. The allocation
change affects accepted refinements and encoded bytes; it is a quality-balance
tradeoff with a measured processing cost.

Both complete outputs contain 25,841 access units and 26,461,184 decoded samples
per channel, including 1024 priming samples and 160 final padding samples.
Every timestamp and packet budget is checked. Independent and native decoding
agree within `1.7881393432617188e-7` over every sample. Actual raw rates are
125.566 and 125.629 kbit/s, respectively; repeated outputs within each version are
byte-identical.

## Reusing unchanged channel costs

The allocator counts the current channel bit costs once, then recounts only
channels affected by an accepted refinement. It retains the existing candidate
cache, exact section/scalefactor costs and search order, using the same stack
storage. A two-band move in one channel triggers one recount after both changes;
a move spanning two channels updates both costs.

A separate complete ten-minute comparison uses the same conditions and
before/after/after/before sequence described above. Each row lists both CPU
measurements and the medians of CPU, elapsed time and peak RSS.

| Relative allocator | Individual CPU, seconds | Median CPU, seconds | Median elapsed, seconds | Median peak RSS, MiB |
| --- | --- | --- | --- | --- |
| Recount every channel | 88.719 / 82.380 | 85.55 | 86.23 | 27.31 |
| Recount affected channels | 83.477 / 83.259 | 83.37 | 84.36 | 27.25 |

The observed median CPU reduction is 2.55%, but the individual ranges overlap and
the reverse pair favors the previous build. This experiment does not establish
a repeatable throughput improvement. It is a separate measurement batch from
the absolute/relative comparison; their medians do not form a controlled
three-version comparison.

All four complete transport files have the same SHA-256,
`21ff50d1c6bf48e8ef9c1e52e39d808f408b7a43953cd7fde25bd7aeafc096d8`.
The nine signals, five SQAM recordings and short HLS case also retain identical
output bytes after cost reuse, so their quality results above are unchanged.

Frequency masking, rate allocation beyond the bounded local search and perceptual
listening acceptance remain open.

## Refining zero-quantized bands

A quiet spectral band can quantize to zero across more than four consecutive
scales. After ordinary refinement stalls, the allocator computes the coarsest
scale at which a coefficient becomes nonzero, then measures up to four candidates
from that boundary. For the nearest-magnitude quantizer, the zero-to-one boundary
is `abs(coefficient) > 2^((scale - 100) / 4) / 2`; equality remains zero.

Candidate selection still measures complete channel syntax and shares the existing
total iteration bound. The operation uses stack state and the existing spectral
workspace. It can recover a representable quiet component with spare packet
capacity without stepping through an arbitrarily long sequence of identical
zero reconstructions.

Managed and Native AOT checks cover all twelve sample rates, long and maximally
separated short groups, mono/stereo syntax, silent coefficients and exact bit
ceilings. The preceding allocator fails the quiet-component check at every
sample rate. Independent exhaustive boundary checks cover all 256 scales,
positive/negative ties and adjacent floating-point values.

A complete original-rate corpus contains five SQAM recordings, recorded speech,
impulses and three channel-balance inputs: 228.2323 seconds, with 19,556 access
units across both versions. Every packet ceiling and every decoded sample passes;
independent and Native decoding differ by at most `1.7881393432617188e-7`.
Seven complete AAC payloads are byte-identical. The other three have per-channel
SNR changes from -0.003782 to +0.006090 dB; the exactly silent channel remains zero.
These results establish the scoped quantization behavior, with listening
acceptance still open.

All ten complete public CLI transports are byte-identical between managed and
Native AOT builds. Their AAC payloads match the independently verified fixed-plan
outputs exactly. The public 5.1-to-stereo HLS check also produces nine identical
files in both builds.

The same ten complete raw-PCM/fixed-window-plan encode operations were measured
as sequential Linux x64 Native AOT process pairs on one CPU core:

| Measurement across ten recordings | Before | After |
| --- | ---: | ---: |
| Sum of process CPU seconds | 23.422441 | 24.222729 |
| Largest process peak RSS, KiB | 41728 | 41600 |

The observed CPU cost increases by 3.42%. Each recording has one process per
version on the shared host; this establishes no repeatable throughput or memory
improvement and does not measure the complete CLI conversion pipeline.

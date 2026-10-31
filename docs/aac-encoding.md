The AAC source module encodes contiguous mono/stereo Float32 PCM into AAC-LC raw
access units. The implementation is managed C# and uses the same normative band
and Huffman tables as the decoder. The forward MDCT reuses its FFT transform.

## Stream contract

```csharp
using AacEncoder encoder = AacEncoder.Create(format, new(1, format.SampleRate),
    allocator, new AacEncoderOptions { BitRate = 128000 });

foreach (AudioFrame frame in pcmFrames)
{
    if (encoder.TrySend(frame, token) != CodecInputStatus.Accepted)
    {
        throw new InvalidOperationException("Drain encoded packets before sending more PCM.");
    }
    DrainAvailablePackets(encoder);
}
encoder.CompleteInput();
DrainAvailablePackets(encoder);
```

`DrainAvailablePackets` consumes `Receive()` while its status is `Available`,
writes and disposes each caller-owned `MediaPacket`, and stops at `InputRequired`
or `EndOfStream`. Complete input and drain to `EndOfStream` before reading final
padding metadata. The packet's `Stream` configuration is an AudioSpecificConfig
for the configured rate and channel layout; it can be passed to `MpegTsWriter`.
`Mediana.Formats.Audio.AacConfiguration.Create` and `ToAudioSpecificConfig` also expose configuration
construction separately.

Inputs require interleaved Float32 storage. Mono accepts `mono` or `front-center`;
stereo requires `front-left`, `front-right` in that order. Rates are 96000, 88200,
64000, 48000, 44100, 32000, 24000, 22050, 16000, 12000, 11025 or 8000 Hz. No rate or
channel conversion is implicit. Use the processing module to convert PCM storage
or explicitly resample/mix before encoding.

PCM timestamps must be known, contiguous and exactly representable in the output
clock. Its numerator must be one and its denominator a multiple of the sample
rate. Gaps, repeated timestamps, changed formats and nonfinite samples fail.
Finite headroom is retained when representable by the AAC quantizer; the encoder
does not clip it. Magnitudes outside the available quantizer range fail.

Each access unit represents 1024 decoded samples per channel, with PTS equal to
DTS. Sine-window overlap introduces 1024 samples of priming. The first packet's
PTS is the first input PTS minus that delay. A final partial input block is zero
padded, and one final analysis block releases the overlap tail. For a nonempty
input, the packet count is `ceil(inputSamples / 1024) + 1`. `PrimingSamples`,
`InputSamplesPerChannel` and final `PaddingSamples` describe the exact sample
accounting. Decoded output includes priming and padding; consumers must represent
or apply the intended trimming explicitly. Empty input produces no packets.

The encoder retains at most one input lease, bounded by
`MaximumInputSamplesPerChannel` (default 65536), and uses the supplied allocator
for analysis storage and returned packets. Its PCM workspace holds 3072 samples
per channel: previous analysis data, current input and one 1024-sample lookahead
block. The first packet requires 2048 input samples unless input is completed
earlier. This decision lookahead does not add coded priming samples or alter PTS.
Release returned packets as processing
proceeds. They survive reset and encoder disposal. `Reset` releases retained input
and analysis state, discards the timeline and permits a new sequence. Failure or
cancellation faults the encoder until reset or disposal. Operations and disposal
must not overlap.

## Bit budget and quality limits

`BitRate` is a raw AAC bit ceiling distributed over 1024-sample frames; it defaults
to 128000 bits/second for the whole stream. Fractional budgets are carried between
frames and rounded down to complete output bytes. Transport/ADTS overhead is
additional. The requested budget must fit syntax and the AAC element buffer bound:
6144 bits for mono or 12288 bits for a channel pair. Stereo channels share the
complete element budget; an individual channel need not use half of it.
The minimum budget includes eight independent short-window groups at the selected
sample rate. Unsupported budgets fail encoder creation before input is retained.
The output is variable bitrate: silence can use substantially less
than the ceiling. There is no bit reservoir, constant-rate fill or target-file-size
guarantee.

Analysis selects long, long-start, eight-short and long-stop sine windows.
Each frequency band has a
separate quantizer scale. A coefficient selects the closer of the two adjacent
reconstructed magnitudes, using the normative `magnitude^(4/3)` reconstruction
and quarter-power gain. Spectral codebooks 1–11 are selected per band; zero bands
omit spectral data and scale symbols. Scale differences skip zero bands and must
remain within −60 through +60; reconstructed scales remain within 0 through 255.
Stereo spectra have independent gain, scale-difference and section syntax within
a common window, while their bit allocation is shared.

Rate control first finds a uniform-scale seed within the bit ceiling. Bounded
local refinement then spends unused bits or trades precision between bands when
that reduces total squared spectral reconstruction error. Each move counts actual
scale Huffman symbols, section runs, spectral tuples, signs and escapes. Search
examines up to four adjacent scales in each direction and accepts at most
32 moves for mono or 64 moves across a stereo pair. Pair search considers up to
four refinement and four coarsening candidates. It retains a valid budget after
every accepted move; it does not claim a globally optimal allocation. Candidate
costing recounts only changed channels and preserves each channel's scale history.
Powered magnitudes reuse transform scratch storage. Analysis storage reserves
128 band slots per channel to accommodate up to eight short-window groups.

Minimizing absolute squared spectral error favors louder channels. It does not
preserve equal relative distortion or a separate quality floor per channel.

Auditory masking, mid/side or intensity encoding, TNS,
PNS encoding and bit-reservoir control remain open. This allocation minimizes
an unweighted spectral error measure rather than a psychoacoustic noise threshold.
It does not establish production listening quality or
parity with established encoders. Broader real music and speech testing remains
required.

Syntax, scale reconstruction and filterbank rules follow ISO/IEC 13818-7:2004,
clauses 6–11, clause 15 and Annexes A/C, and ISO/IEC 14496-3:2009 AAC-LC rules.

## Automatic window selection

The detector adapts sub-block peak analysis from
[Fan et al., AES Convention 155, paper 131, section 3.4](https://aes.org/publications/elibrary-page/22285.pdf).
Each channel has a first-order high-pass filter at one sixth of its sample rate
and peak histories at 128, 256 and 512 samples. The peak floor is 1500 on the
PCM16 scale; relative thresholds are 0.07 for 128 samples and 0.4 for the coarser
layers. The normalized cutoff and 1024-sample analysis interval are implementation
choices; the published study does not validate this encoder's listening quality.

An attack in either channel selects short windows for both overlapping transforms.
Lookahead permits a preceding long-start window. The selector checks continuity
of its decisions and emits only legal transitions. An initial short window uses
the zero-initialized overlap. At EOF, padded lookahead releases the same complete
tail as long-window encoding. Reset discards both detector histories and decisions.

Adjacent short windows share scale factors while their windowed PCM energies
remain within a factor of four in every channel. A quiet channel can split a group
independently of a louder channel. Digital silence groups together; a transition
from zero to nonzero energy separates groups. Group lengths sum to eight.

Complete independent decoding compares the preceding long-window encoder with
automatic selection at the same raw ceilings:

| Source | Ceiling, kbit/s | Long SNR, dB | Automatic SNR, dB | Long/automatic actual raw kbit/s |
| --- | --- | --- | --- | --- |
| Mono 48 kHz | 64 | 19.10 | 19.14 | 61.64 / 61.88 |
| Stereo 44.1 kHz | 128 | 19.67 | 19.53 | 124.46 / 125.55 |
| Stereo 48 kHz | 192 | 23.24 | 23.16 | 189.41 / 189.43 |
| Silent right channel | 96 | 62.59 | 60.07 | 94.13 / 94.13 |
| Right channel at 5% amplitude | 96 | 53.93 | 51.35 | 94.13 / 94.13 |
| Alternating active channels | 96 | 33.96 | 39.32 | 94.13 / 94.17 |
| Generated music | 128 | 68.47 | 75.12 | 122.27 / 122.31 |
| Recorded speech, mono | 32 | 23.63 | 24.19 | 31.20 / 31.25 |
| Impulses | 96 | 6.16 | 28.92 | 11.06 / 7.44 |

For the impulse source, error energy in the 1024 source-silent samples preceding
each noninitial onset falls from 0.03687 to 0.000895 in the left channel and from
0.005586 to 0.000639 in the right channel. Peak pre-onset error falls from
0.04489 to 0.00980 and from 0.04848 to 0.00723 respectively.
These measurements include the final onset immediately before EOF.

Short windows trade frequency resolution for time resolution. In the stationary
unequal-amplitude source, left/right SNR changes from 55.02/34.38 to 52.48/31.66 dB.
That regression remains a quality issue. Automatic selection reduces measured
impulse spreading; it does not replace perceptual allocation or listening tests.

Five complete [EBU SQAM recordings](https://tech.ebu.ch/docs/tech/tech3253.pdf)
also pass through the public Native AOT FLAC-to-AAC command at a 128 kbit/s
ceiling. All 216 seconds and 9,311 packets per encoder version decode independently;
every raw packet stays within its ceiling. Results retain each channel separately:

| SQAM track | Duration, seconds | Long left/right SNR, dB | Automatic left/right SNR, dB | Long/automatic raw kbit/s |
| --- | --- | --- | --- | --- |
| 27, castanets | 20 | 14.23 / 14.03 | 20.85 / 20.82 | 99.88 / 99.69 |
| 40, harpsichord | 53 | 20.37 / 20.38 | 19.97 / 19.93 | 103.08 / 103.05 |
| 60, piano | 92 | 41.55 / 42.52 | 41.23 / 42.29 | 120.06 / 120.06 |
| 66, wind ensemble | 18 | 23.55 / 24.04 | 23.54 / 24.04 | 98.85 / 98.87 |
| 69, pop | 33 | 24.69 / 24.39 | 24.96 / 24.80 | 108.16 / 108.24 |

The real percussive recording improves in both channels, while harpsichord and
piano retain small objective regressions. No listening panel or perceptual
transparency result is established by these SNR measurements.

## Console path

```sh
mediana -i input.wav output.ts --audio-codec Aac --audio-bit-rate 128000
mediana -i input.mp3 output.ts --audio-codec Aac
```

This selects audio-only MPEG-TS containing newly encoded AAC-LC. The first audio
stream is selected unless `--stream` is supplied. Supported PCM/AIFF/FLAC/MP3 and
MP4 AAC inputs pass through the existing decoder and explicit Float32 storage
conversion; the source rate and channel order are preserved. Unsupported rates
or layouts fail. Video encoding options cannot be combined with this mode. The
destination must be new; failures and cancellation discard its temporary file.
Progress reports source media time, speed and bytes through the standard logger.
A known coded duration enables a smoothed remaining-time estimate; unknown totals
omit the ETA field.

TS retains all priming and padded samples and uses the existing one-second
transport timestamp offset. It does not carry gapless trimming metadata. Standalone
ADTS output and MP4 audio writing remain open.

The [HLS encoding command](hls-encoding.md) can also combine this encoder with AVC
video variants through `--audio-stream 1 --audio-codec Aac`. That path explicitly
applies the input MP4 edit to decoded PCM and preserves its movie origin before
encoding. It retains the new encoder's priming and padding and shares the encoded
audio across all widths. The standalone audio-only command above retains the
source decoder's complete media timeline.

## Verification

Forward MDCT tests compare every coefficient at both supported transform sizes
with the direct cosine formula. Entropy tests cover signed tuples, escape
magnitudes through 8191 and every DPCM scale symbol. Quantizer tests check both sides of every reconstruction
decision boundary at scales 0, 100 and 255. Band tests compare exact written bit
counts, round-trip every coefficient and preserve zero-band scale semantics.
Shared-budget tests cover either active channel, silence, unequal amplitudes,
and a channel-pair budget above the mono element capacity.
Different PCM block sizes produce identical packets;
state tests cover backpressure, timing gaps, bounds, cancellation, reset, delay,
padding and output ownership.

Four complete mono/stereo WAV sequences produce 162 packets. One includes a
silent or quiet second channel followed by alternating channel activity.
Every sample decoded by Mediana agrees with independently decoded Float32
references within 0.00002, including priming and padded tails. The same verifier
runs in Native AOT.
[The reference manifest](../tests/Mediana.Tests/Data/Aac/ENCODING.json) records input,
output and reference hashes and the complete independent decoding commands.

The shared analysis and packet encoder also support long-start, eight-short and
long-stop windows, sine/KBD shapes and grouped short spectra. A separate controlled
mono/stereo sequence exercises every permitted window-sequence transition and
four short-window groupings. Its 28 packets produce 14,336 decoded samples per
channel in each stream, including 1024 priming samples and the complete tail.
Every decoded sample agrees with the independent reference within 0.00002;
the verifier also checks window declarations, packet budgets, timestamps and
storage release. [Its manifest](../tests/Mediana.Tests/Data/Aac/WINDOWS.json)
records the original signal, exact window decisions and reference hashes.
These controlled decisions verify the packet implementation independently of
automatic selection. Public tests also cover every supported sample rate,
initial/final attacks, both channels, partial blocks, input block independence,
minimum budgets and reset replay. A separate complete 96 kHz MP4 sequence has
zero in its container sample-rate field and a definitive AAC configuration;
all 14 packets and 14,336 PCM samples match independent decoding.
An additional independent Native AOT matrix checks complete mono/stereo streams
at all 12 rates: 336 packets, every decoded sample, and a maximum absolute
Float32 difference of 0.000000123 from the independent decoder.

Native and managed commands produce identical TS bytes on the complete tone,
noise, unequal-channel, speech and impulse checks. A complete ten-minute 44.1 kHz
stereo PCM sequence produces 25,841 AAC
packets and 26,461,184 decoded samples per channel, including 1024 priming and
160 padding samples. Every packet's PTS and raw bit budget is checked, and the
complete decoded PCM agrees with the independent decoder within 0.00000018.
An in-progress SIGINT returns 130 and removes the temporary output file.
Standalone Native AOT commands also convert complete MP3, 24-bit FLAC, 24-bit
AIFF and MP4 AAC inputs; every resulting TS decodes independently without errors.
A 44117 Hz FLAC input is rejected without publishing output, preserving the
explicit supported-rate contract.

A Native AOT comparison encodes that complete ten-minute generated stereo input
at a 128 kbit/s frame ceiling. The baseline uses long windows; the current encoder
selects windows and grouping automatically. Both retain shared channel budgets and
candidate-cost caching, use identical surrounding CLI sources, .NET 10.0.13 on
Linux x64 and affinity to one CPU core. Four complete runs use
before/after/after/before order. Verification covers every output timestamp,
packet budget and independently decoded PCM sample from both versions.

| Process measurement | Long windows | Automatic windows |
| --- | --- | --- |
| Median CPU, seconds | 101.43 | 86.31 |
| CPU range, seconds | 97.92–104.93 | 84.96–87.65 |
| Median wall time, seconds | 103.31 | 88.48 |
| Median peak RSS, MiB | 27.00 | 27.13 |
| Peak RSS range, MiB | 26.88–27.13 | 27.00–27.25 |
| Actual raw AAC, kbit/s | 124.92 | 125.57 |

The observed median CPU reduction is 14.91%. There are two runs per binary on a
shared host; this result describes this input and does not establish a general
speedup or a comparison with another encoder. Automatic selection changes the
encoded data and actual rate. Repeated outputs are byte-identical within each
version. The extra lookahead requires 8192 accounted bytes per channel; the full
stereo workspace is 107008 bytes. Process RSS includes other memory.

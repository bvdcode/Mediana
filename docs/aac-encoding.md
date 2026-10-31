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
for analysis storage and returned packets. Release returned packets as processing
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
The output is variable bitrate: silence can use substantially less
than the ceiling. There is no bit reservoir, constant-rate fill or target-file-size
guarantee.

Analysis uses long sine windows for every frame. Each frequency band has a
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

The before/after checks compare independent half-budget stereo allocation with
shared allocation at the same raw ceiling. Inputs include generated signals and
an existing recorded speech excerpt, independently decoded and aligned by the
declared delay:

| Source | Ceiling, kbit/s | Previous SNR, dB | Current SNR, dB | Previous/current actual raw kbit/s |
| --- | --- | --- | --- | --- |
| Mono 48 kHz | 64 | 19.10 | 19.10 | 61.64 / 61.64 |
| Stereo 44.1 kHz | 128 | 19.63 | 19.67 | 124.23 / 124.46 |
| Stereo 48 kHz | 192 | 23.27 | 23.24 | 189.22 / 189.41 |
| Silent right channel | 96 | 46.90 | 62.59 | 48.99 / 94.13 |
| Right channel at 5% amplitude | 96 | 46.90 | 53.93 | 94.12 / 94.13 |
| Alternating active channels | 96 | 33.68 | 33.96 | 56.55 / 94.13 |
| Generated music | 128 | 68.24 | 68.47 | 122.58 / 122.27 |
| Recorded speech, mono | 32 | 23.63 | 23.63 | 31.20 / 31.20 |
| Impulses | 96 | 3.81 | 6.16 | 9.21 / 11.06 |

The metric is unweighted source SNR after removing priming and padding. Increased
use of the available bit ceiling contributes to the improvement; these are not
equal-size output comparisons. Balanced stereo changes are small, including a
0.02 dB reduction in one case. Mono output remains byte-identical. The impulse
result remains poor and does not establish transient quality.

The channel tradeoff is visible in the 5%-amplitude case: left-channel SNR rises
from 46.90 to 55.02 dB while right-channel SNR falls from 46.55 to 34.38 dB.
Minimizing the absolute sum of squared errors favors the louder channel; it does
not preserve equal relative distortion or a separate quality floor per channel.

Automatic short-window selection, auditory masking, mid/side or intensity encoding, TNS,
PNS encoding and bit-reservoir control remain open. This allocation minimizes
an unweighted spectral error measure rather than a psychoacoustic noise threshold.
It does not establish production listening quality, transient performance or
parity with established encoders. Broader real music and speech testing remains
required.

Syntax, scale reconstruction and filterbank rules follow ISO/IEC 13818-7:2004,
clauses 6–11, clause 15 and Annexes A/C, and ISO/IEC 14496-3:2009 AAC-LC rules.

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
These controlled decisions verify the packet implementation. The public encoder
continues to select long sine windows; automatic transient detection and the
required decision lookahead remain open.

Native and managed commands produce identical TS bytes on the complete tone,
noise, unequal-channel, speech and impulse checks. A complete ten-minute 44.1 kHz
stereo PCM sequence produces 25,841 AAC
packets and 26,461,184 decoded samples per channel, including 1024 priming and
160 padding samples. Every packet's PTS and raw bit budget is checked, and the
complete decoded PCM agrees with the independent decoder within 0.00000015.
An in-progress SIGINT returns 130 and removes the temporary output file.
Standalone Native AOT commands also convert complete MP3, 24-bit FLAC, 24-bit
AIFF and MP4 AAC inputs; every resulting TS decodes independently without errors.
A 44117 Hz FLAC input is rejected without publishing output, preserving the
explicit supported-rate contract.

A Native AOT comparison encodes that complete ten-minute generated stereo input
at a 128 kbit/s frame ceiling. The baseline allocates independent half-budgets;
the current implementation shares the budget and caches candidate costs until
their channel changes. Both use identical surrounding CLI sources, .NET 10.0.13
on Linux x64 and CPU affinity to one core. Four complete runs alternate in
before/after/after/before order.

| Process measurement | Independent budgets | Shared budget with cost cache |
| --- | --- | --- |
| Median CPU, seconds | 94.66 | 96.12 |
| CPU range, seconds | 91.01–98.31 | 95.33–96.92 |
| Median wall time, seconds | 96.41 | 98.54 |
| Median peak RSS, MiB | 27.00 | 27.19 |
| Actual raw AAC, kbit/s | 124.63 | 124.92 |

The host is shared and the CPU ranges overlap; the observed median CPU increase
is 1.55%. Repeated outputs are byte-identical within each version. Candidate-cost
caching also preserves every output byte of the preceding shared-budget
implementation, including the complete ten-minute TS. That implementation's
separate four-run measurement used 111.59 seconds median CPU before the cache.

A further four-run ABBA comparison checks the shared analysis and packet path
against the preceding shared-budget implementation, with automatic window
selection still disabled. Both Native AOT binaries encode the same complete
ten-minute input at the same ceiling on CPU 2. All four TS outputs are
byte-identical; every packet timestamp, budget and independently decoded PCM
sample is checked again.

| Process measurement | Previous long-window path | Shared analysis and packet path |
| --- | --- | --- |
| Median CPU, seconds | 95.65 | 92.33 |
| CPU range, seconds | 94.42–96.88 | 90.44–94.21 |
| Median wall time, seconds | 97.69 | 95.69 |
| Median peak RSS, MiB | 27.19 | 26.94 |

The observed median CPU reduction is 3.47%. There are two runs per binary on a
shared host; this measurement does not establish a general speedup or predict the
cost of automatic short-window selection. The workspace's additional band slots
require 128 extra accounted bytes per channel; process RSS includes other memory.

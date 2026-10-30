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
`AacConfiguration.Create` and `ToAudioSpecificConfig` also expose configuration
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
additional. The requested budget must fit syntax and the AAC 6144-bit per-channel
buffer bound. The output is variable bitrate: silence can use substantially less
than the ceiling. There is no bit reservoir, constant-rate fill or target-file-size
guarantee.

Analysis currently uses long sine windows for every frame. Each channel has one
global quantizer scale; nonuniform quantization rounds magnitude to the nearest
integer. The encoder selects spectral codebooks 1–11 per band and skips zero bands.
It increases quantizer scale until the exact Huffman/sign/escape/section bit count
fits the channel budget. Stereo channels are independent within a common window.

Short-window switching, psychoacoustic per-band allocation, mid/side or intensity
encoding, TNS and PNS encoding remain open. This first path does not promise
production perceptual quality or parity with established encoders. The three
generated tone/noise/impulse fixtures measure approximately 16.3, 16.4 and 20.7 dB
unweighted source SNR at their respective 64/128/192 kbit/s ceilings after removing
declared delay/padding. Simple stationary-tone tests exceed 25 dB. These measurements
characterize those inputs only; they are not a listening-quality assessment.

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

TS retains all priming and padded samples and uses the existing one-second
transport timestamp offset. It does not carry gapless trimming metadata. Standalone
ADTS output, MP4 audio writing and encoding audio within HLS variants remain open.
The existing HLS command can still copy an original AAC/MP3 track while encoding
video; that operation is separate from this new audio encoder.

## Verification

Forward MDCT tests compare every coefficient at both supported transform sizes
with the direct cosine formula. Entropy tests cover signed tuples and escape
magnitudes through 8191. Different PCM block sizes produce identical packets;
state tests cover backpressure, timing gaps, bounds, cancellation, reset, delay,
padding and output ownership.

Three complete mono/stereo WAV sequences produce 55 packets. Every sample decoded
by Mediana agrees with independently decoded Float32 references within 0.00002,
including priming and padded tails. The same verifier runs in Native AOT.
[The reference manifest](../tests/Mediana.Tests/Data/Aac/ENCODING.json) records input,
output and reference hashes and the complete independent decoding commands.

Native and managed commands produce identical TS bytes for all three reference
inputs. A complete ten-minute 44.1 kHz stereo PCM sequence produces 25,841 AAC
packets and 26,461,184 decoded samples per channel, including 1024 priming and
160 padding samples. Every packet's PTS and raw bit budget is checked, and the
complete decoded PCM agrees with the independent decoder within 0.00000015.
An in-progress SIGINT returns 130 and removes the temporary output file.
Standalone Native AOT commands also convert complete MP3, 24-bit FLAC, 24-bit
AIFF and MP4 AAC inputs; every resulting TS decodes independently without errors.
A 44117 Hz FLAC input is rejected without publishing output, preserving the
explicit supported-rate contract.

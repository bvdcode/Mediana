# HE-AAC decoding

`AacDecoderFactory` decodes AAC-LC cores with spectral band replication (SBR)
and parametric stereo (PS) entirely in managed code. The same factory serves
ordinary library clients, indexed decoding and the CLI. SBR generates and
adjusts high-frequency subbands; PS reconstructs independent left and right
signals from a monaural core. Neither tool is implemented as PCM duplication
or sample-rate conversion.

Supported signaling includes forward audio object types 5 and 29,
backward-compatible SBR/PS sync extensions and in-band implicit extensions.
The core rate and channel count in `AacConfiguration` retain their original
meaning. SBR output uses the configured extension rate; implicit SBR uses
twice the core rate. PS produces two decoded channels from one core channel.
`AudioFrame.Format` describes the actual decoded output.

Both dual-rate and single-rate synthesis are implemented. A 24 kHz core can
produce 48 kHz output through the 64-band QMF bank or explicitly signaled
24 kHz output through the 32-band synthesis bank. Single-rate decoding does
not first synthesize and then downsample 48 kHz PCM.

The PS path supports 10, 20 and 34 parameters, coarse and fine IID grids,
both ICC mixing procedures, fixed and variable envelope borders, IPD/OPD
phase parameters and resolution changes. Its hybrid filterbank, transient
reduction, decorrelating delays, all-pass filters and mixing histories persist
across access units. Before the first independently coded PS parameter set,
the mono signal is presented in both channels, as specified for PS startup.
Empty envelopes hold enabled parameters; disabled parameters use their default
values. SBR CRC-10 payloads are verified before their syntax is consumed.

Each compressed packet contains one complete AAC raw data block. Output is
interleaved native-endian float32 PCM. Timestamps remain in the source clock;
output duration uses the actual sample count and rate. Decoder priming and
container edit trimming are separate from spectral reconstruction. Reset
clears the AAC overlap, SBR and PS histories. Cancellation or failure requires
reset or disposal before more input is accepted.

Indexed restart inspection parses syntax without constructing a DSP workspace.
An SBR restart needs a header with independently coded initial envelope and
noise parameters, followed by two decoded warmup packets. PS additionally
requires an independent PS header and parameter set. Explicit PS configurations
therefore reject an SBR-only restart anchor. These restart rules restore a
fresh decoder at an eligible anchor; they do not represent an exact snapshot
of a decoder that has processed the complete preceding stream.

When monaural opening configuration leaves PS unspecified, the optional generic
origin-inspection capability examines packet zero once before indexed seeking.
It retains no payload. A detected PS extension constrains later restart anchors;
absence of that extension does not permanently declare that the stream has no
PS. Stereo cores and complete PS signaling need no extra origin read. Ordinary
sequential decoding performs no additional origin read. Indexed reads otherwise
remain restricted to the chosen restart window and the requested output.

All persistent filter storage uses the caller's `MediaBufferAllocator`.
The complete real PS sequence tests fit in a 256 KiB media-buffer budget,
keep their live state constant and release it on disposal. Retained output
frames remain valid after reset and decoder disposal.

The [HE-AAC references](../tests/Mediana.Tests/Data/AacSbr/README.md) cover
independent mono/stereo single-rate and dual-rate reconstruction and indexed
MP4/ADTS restarts. The [PS references](../tests/Mediana.Tests/Data/AacPs/README.md)
distinguish unmodified independent decoder interoperability from the separate
normative mathematical control. Complete real HE-AAC v2 sequence comparisons
achieve 63.70–102.76 dB signal/error ratio; the changing-parameter mathematical
control achieves 103.88 dB. Analytic tests also check hybrid reconstruction,
energy conservation, IID ratios, phase smoothing and alias mappings.

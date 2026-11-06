# Opus entropy reference

`range-decoder.bin` records 128 deterministic compressed-frame inputs from zero to
1,275 bytes and 65,071 successful entropy operations, evaluated with libopus 1.6.1.
It covers binary probabilities, inverse cumulative distributions, general frequency
models, uniform integers through the unsigned 32-bit range, reverse raw bits,
lookahead overlap and the required zero extension after the frame ends.

Each successful operation records its returned symbol, final arithmetic range,
whole-bit usage and fractional usage in eighths of a bit. An additional terminal
entry records the reference's corrupt uniform-integer indication. Separate tests
require an exception for that condition and for raw-bit overread. The reference
implementation is not linked to the library or required to run the tests.

The little-endian file starts with `OPUSRNG1` and a 32-bit case count. Each case
contains a 32-bit byte length, its input bytes, a 32-bit operation count and three
32-bit initial state values: arithmetic range, whole bits and eighth bits. Each
operation is one byte for its kind and six 32-bit values: parameter, result,
arithmetic range, whole bits, eighth bits and error indication. Kinds 0 through 4
select binary probability, inverse cumulative model, uniform integer, raw bits and
frequency model respectively. Probability models are fixed in the test reader.

SHA-256: `ae97af08bd287c13138e06ed6f9dba98176eefb0e93830d2a3280a65ba15fdaa`.

The entropy syntax follows [RFC 6716 section 4.1](https://www.rfc-editor.org/rfc/rfc6716.html#section-4.1).
Reference releases are available from [Xiph.Org](https://downloads.xiph.org/releases/opus/).

`packet-layout.bin` records 4,004 packet inputs parsed by libopus 1.6.1: 2,372
valid layouts and 1,632 rejected inputs. It covers all 32 configurations, both
channel flags, all four packing codes, CBR/VBR, empty and maximum-length frames,
multi-byte padding, truncated data and self-delimited multistream boundaries.

The little-endian file begins with `OPUSPKT1` and a 32-bit case count. Each case
contains a 32-bit packet length, its bytes, one byte selecting self-delimiting
framing and a signed 32-bit reference frame count or error. Successful entries add
32-bit consumed-byte and samples-per-frame counts, followed by each frame's 32-bit
offset and length. Tests compare every range and verify that rejected packets leave
the caller's range destination unchanged.

SHA-256: `8a92a08eaa486acde862012a9a01898c5caf4bce0925ec4d6b078e031bf59098`.

`pulse-vectors.bin` records 13,574 signed pyramid vector quantization codewords
evaluated with libopus 1.6.1. Dimensions range from 2 to 256, pulse counts from 1
to 2,048, and representable codebooks approach the 32-bit entropy interval. Small
codebooks include every rank; larger ones include boundary, midpoint and
deterministically sampled ranks. Tests compare every integer coefficient and its
squared norm. Separate exact combinatorial tests cover the zero-pulse and
single-dimension cases and reject codebooks beyond the 32-bit range.

The little-endian file starts with `OPUSPVQ1` and a 32-bit case count. Each case
contains four unsigned 32-bit values: dimension count, pulse count, codebook size
and rank, followed by the signed 32-bit coefficients in dimension order.

SHA-256: `bab6a5c2e66f78fdc61513514fb22362cd0d1738132575170ebb343c9283eb57`.

The codeword enumeration follows [RFC 6716 section 4.3.4.2](https://www.rfc-editor.org/rfc/rfc6716.html#section-4.3.4.2).

`laplace-decoder.bin` records 5,257 signed energy residuals from libopus 1.6.1,
including arithmetic ranges and whole/fractional bit usage after each operation.
Inputs include empty and truncated frames, sequentially changing probability
models, boundary probabilities, the decaying portion of the distribution and
its minimum-probability tails.

The little-endian file starts with `OPUSLAP1` and a 32-bit case count. Each case
contains a 32-bit byte length, input bytes and a 32-bit operation count. Operations
contain six 32-bit values: zero probability, decay, signed residual, arithmetic
range, whole bits and eighth bits. Probabilities use Q15 precision.

SHA-256: `19acb99bf22c604f0185255e1e718a08388f0666fbc1eded2d289217e7b6555b`.

`energy-decoder.bin` records all three energy reconstruction stages for 768
libopus 1.6.1 cases: all four CELT frame sizes, mono/stereo, intra/inter prediction,
full-band and hybrid-band starts, partial bandwidths, and frames from zero to
1,275 bytes, with random, saturated and alternating input bytes. Tests require
bit-exact float32 energies and arithmetic range/bit
usage after coarse reconstruction, raw-bit refinement and final priority bits.
Bands outside the requested range retain their initial values.

The little-endian file begins with `OPUSENE1` and a 32-bit case count. Each case
contains the input byte count and bytes; 32-bit channel count, start band, end
band, frame-size shift and intra flag; 21 float32 starting energies per channel;
21 32-bit fine-bit counts; and 21 32-bit priorities. The three output states each
contain 21 float32 energies per channel and the 32-bit arithmetic range, whole
bits and eighth bits.

SHA-256: `3f2d7c70d2ae6513b92a6fb8d72901f217e3f72b69a9452deb2e1cd51273adce`.

Energy reconstruction follows [RFC 6716 section 4.3.2](https://www.rfc-editor.org/rfc/rfc6716.html#section-4.3.2).

`band-allocation.bin` records 4,096 allocation decisions evaluated with libopus
1.6.1. Cases cover mono/stereo, every CELT frame size, full and hybrid band starts,
partial bandwidth, trims 0 through 10, dynamic band boosts, constrained capacity
and frame lengths from zero to 1,275 bytes. Tests compare shape budgets, fine-bit
counts and priorities in every band, the coded bandwidth, intensity/dual stereo,
remaining balance, arithmetic range and whole/fractional bit usage. Tests also
verify that repeated planning does not allocate managed storage.

The little-endian file starts with `OPUSALC1` and a 32-bit case count. Each case
contains a 32-bit input length and input bytes; 32-bit channel count, frame shift,
first band, end band, available eighth bits and trim; and 21 32-bit boosts. Results
contain seven 32-bit fields: coded end band, intensity start, dual-stereo flag,
remaining balance, arithmetic range, whole bits and eighth bits. Finally, 21
triples record the shape eighth bits, fine bits and final priority for each band.

SHA-256: `fe06f5378196fb4ec2a0257d96cb5c430517b6083e40c49d877bc4dacd41c099`.

Allocation follows [RFC 6716 section 4.3.3](https://www.rfc-editor.org/rfc/rfc6716.html#section-4.3.3).

`frame-syntax.bin` records 1,024 sequential CELT frame prefixes evaluated by the
libopus 1.6.1 decoder immediately before spectral shape reconstruction. These
tests combine silence, post-filter parameters, transients, intra prediction,
coarse energies, time/frequency resolution changes, spreading, dynamic boosts,
trim, anti-collapse reservation, allocation and fine energies. Frame sizes,
channels, bandwidths, hybrid band starts and initial energies vary; compressed
inputs include random and constant bytes. Every result and the final arithmetic
state are compared exactly, including float32 energy bits. Shape reconstruction
and PCM synthesis are outside this reference's coverage.

The little-endian file starts with `OPUSCEL1` and a 32-bit case count. Each case
contains a 32-bit compressed byte count and bytes; 32-bit channels, frame shift,
first band and end band; and 21 initial float32 energies per channel. Results are
32-bit fields for silence, transient, intra, post-filter period, float32 gain,
tapset, spreading, trim, shape budget, anti-collapse reservation, coded end band,
intensity band, dual stereo, balance, arithmetic range, whole bits and eighth bits.
These are followed by 21 float32 resulting energies per channel and 21 tuples of
32-bit time/frequency resolution, boost, shape eighth bits, fine bits and priority.
Inactive band tuples contain zero.

SHA-256: `056e4effd1fe970aa5c7d4a706731262dc363e9bf747dac472dfc8436361ea83`.

`normalized-vectors.bin` records 3,793 PVQ spectral vectors evaluated with
libopus 1.6.1: valid canonical pulse codebooks, up to 176 coefficients and 128
pulses, all four spreading modes, one to eight time blocks, and gains including
zero. Collapse masks and entropy states must match exactly. Normalized and rotated
coefficients have an absolute error tolerance of 0.000001 for floating-point
trigonometry. Corrupt uniform ranks are tested separately and must leave the
coefficient workspace unchanged. Reused normalization and rotation allocate no
managed storage.

The little-endian file starts with `OPUSVEC1` and a 32-bit case count. Each case
contains a 32-bit input length and input bytes; 32-bit dimension count, pulse count,
block count, spreading mode and float32 gain; 32-bit collapse mask, arithmetic
range, whole bits and eighth bits; and the resulting float32 coefficients.

SHA-256: `eafc50d9d0acebfb4f2337de8febf37ffb480c60590740bb59dd140ad6bc24e1`.

`pulse-costs.bin` records the canonical pulse allocation tables and 49,955 nearest
cost selections from libopus 1.6.1. All 105 combinations of band and split depth
are represented; eight combinations have zero dimensions and no codebook. Tests
compare all pulse counts and eighth-bit costs, and selections for every budget
from -2 to 512 eighth bits, including equal costs and midpoint ties. Repeated
lookup does not allocate managed storage.

The little-endian file starts with `OPUSPUL1` and a 32-bit case count. Each case
contains signed 32-bit band, frame shift and maximum pseudo-pulse index. A negative
index marks an absent codebook. Present codebooks contain pairs of 32-bit actual
pulse counts and eighth-bit costs for index zero through the maximum, followed
by 515 32-bit selected indices in ascending budget order.

SHA-256: `9a7b87d851981229448155d2c215f78471c19e57dcb489d8f2ed91ff4cf27a61`.

`normalized-vectors-16.bin` uses the `OPUSVEC1` layout and the same tolerances.
It adds 1,864 reference vectors with sixteen time blocks, which may result from
time/frequency resolution changes even though a CELT frame has at most eight
short MDCTs. Dimensions, pulse counts, spreading modes and gains vary. This
coverage also runs under Native AOT.

SHA-256: `9a23afc70907d3cdc1aa9ec3cec512bf001ca06ee7709df969fc053a2c5ae104`.

`split-angles.bin` records 8,105 mono/stereo split decisions from libopus 1.6.1,
including triangular, uniform and stereo step probabilities, intensity inversion,
angle resolution, both gains, bit-allocation delta, occupancy masks and exact
entropy state. It also records integer cosine and log-tangent results for all
16,257 angles from 64 through 16,320, covering the range reachable with the
maximum 256 angle subdivisions. Corrupt uniform-integer cases are rejected
separately rather than treated as successful reference results.

SHA-256: `943c668c398aae1ff8cb59ce4ed550206dd2c1eaa88dd8f2b622e0ce114af25b`.

Split reconstruction follows [RFC 6716 section 4.3.4.4](https://www.rfc-editor.org/rfc/rfc6716.html#section-4.3.4.4).

`partitions.bin` records 7,682 recursive mono spectral partitions evaluated with
libopus 1.6.1. Cases cover every canonical band size, all splitting depths,
1–16 time blocks, each spreading mode, exhausted and redistributed bit budgets,
zero/nonzero gain, empty occupancy masks, deterministic noise and spectral folding.
Tests require exact arithmetic state, remaining budget, noise seed and collapse
mask; every normalized coefficient has an absolute tolerance of 0.000001.
Repeated reconstruction reuses its codebook and output buffers without managed
heap allocations and leaves the borrowed folding spectrum unchanged.

SHA-256: `e6ce07590ff8141c3cc154d90dde24cde8cf726373cd5507017665fbad0c69f0`.

`spectral-bands.bin` records 7,782 complete mono bands evaluated with libopus
1.6.1. It adds the time/frequency resolution changes for all four frame sizes,
transient and long transforms, spectral reordering, folding-buffer preparation
and inverse Haar reconstruction. One-dimensional sign-only bands and folding
outputs are included. Entropy state, seed, budgets and masks are exact; normalized
coefficients use 0.000001 absolute tolerance and scaled folding outputs 0.00001.
The same spectral fixture reader verifies both partition and complete-band data.

SHA-256: `3c06b075aea35941f90bd93e62bfbc2c675932325024df1ab06831a1bbac3f32`.

`stereo-bands.bin` records 7,708 joint-stereo bands evaluated with libopus 1.6.1.
It covers intensity boundaries, inversion control, joint mid/side allocation,
the orthogonal two-coefficient representation, sign-only bands, time/frequency
changes and folding. Both reconstructed channels, folding output, entropy state,
noise seed, budgets and collapse masks use the same comparisons as mono bands.
Repeated reconstruction allocates no managed storage and preserves borrowed
folding input.

SHA-256: `4509a70ad0c8a75c51754e5ae4b3ecfe15f765359ca8a49646d1db83d8b7e918`.

`spectral-frames.bin` records 1,963 complete sequential frame-prefix and spectral
reconstructions from libopus 1.6.1, intercepted after its actual all-band decoder.
Cases include all frame sizes, mono/stereo, CELT-only and hybrid-band starts,
partial bandwidths, silence, saturated and deterministic random compressed data.
Tests compare the complete normalized spectrum, every collapse mask, final noise
seed and entropy state. Workspaces begin with NaN values to expose unintended
reads of uninitialized folding data. Spectrum tolerance is 0.000001; all integer
state is exact. Repeated full-frame reconstruction allocates no managed storage.
This fixture ends before anti-collapse, denormalization and inverse transforms.

SHA-256: `34ce6f07c8502d5b52c024f3e92edd56cac32f4423bc77a9a692d6a27b19566c`.

`encoded-spectra.bin` adds 1,152 spectral frames from actual libopus 1.6.1
encoding: all four CELT durations, mono/stereo, narrowband through fullband,
constant and variable bitrate, three bitrate targets, tones, noise, impulses,
chirps, equal and opposed stereo signals, and transitions to/from silence.
Each group retains the reference decoder's complete state across twelve frames;
a state snapshot records the spectrum without changing the subsequent decode.
The coded channel count is read from each packet TOC independently of the configured
output channel count; stereo streams include mono-coded frames. Mono prediction
starts from the maximum energy of the two channel histories. All frames pass the
same exact entropy/seed/mask and coefficient comparisons.

SHA-256: `4afb3f6f81895ff2d7a3f955106c75c95c50bcf772da9894238424ebe5d8e31f`.

`inverse-mdct.bin` records 128 inverse MDCT and overlap transforms from libopus
1.6.1 for 120, 240, 480 and 960 coefficients, interleaved strides 1/2/4/8,
zero, impulse, alternating and deterministic random spectra, several levels and
nonzero preceding overlap. Relative L2 error is at most 0.000002; peak absolute
error is at most 0.0001 times the larger of one and the peak input coefficient.
The transform reuses a budgeted immutable plan and a caller-owned workspace of
one float per coefficient, with no managed allocations per call. Cancellation
preserves output/workspace and disposal releases the plan's allocation.

SHA-256: `deb8b0980870297afab96b70b84c56831d9e95b3c37a60678ce35ff75ccdb225`.

`spectrum-restoration.bin` records 512 transient anti-collapse and band-energy
restoration cases from libopus 1.6.1. Mono/stereo, hybrid-band starts, partial
bandwidths, prior energy histories, occupied/missing short blocks, bit depths,
extreme energy differences and silence are included. Normalized coefficients
use 0.000001 absolute tolerance. Scaled frequency coefficients use 0.000001
absolute plus 0.000001 relative tolerance. The floating energy floor is verified
at and immediately below exponent -50. Repeated processing allocates no managed
storage.

SHA-256: `c4148226f356bde1e7d49d5369650cdf97c69e8e4ccc3d85e6150a40645315ab`.

`packet-configurations.bin` records the coded channel count, frame duration at
48 kHz and bandwidth for all 256 TOC bytes, evaluated with the libopus 1.6.1
public packet functions. Separate mode-boundary tests cover SILK, hybrid and CELT
selection, and packet packing bits do not change the coding configuration.

The little-endian file begins with `OPUSTOC1` and a 32-bit case count. Each case
contains the TOC byte followed by three 32-bit integers: coded channels, samples
per frame and bandwidth (narrowband through fullband as values zero through four).

SHA-256: `3901c777f70398c3f396156981acece497ebb5a53b61781f5bfe3f81025b7950`.

`synthesis.bin` records 128 libopus 1.6.1 synthesis cases covering every CELT
frame size, long/transient transforms, four bandwidths, hybrid-band starts,
mono/stereo coding and output-channel combinations, nonzero incoming overlap,
pitch-filter transitions and steady filters. Signal histories, normalized
spectra, band energies and de-emphasis memory vary independently. Raw synthesis
has relative L2 tolerance 0.000003 and peak tolerance 0.0001 of the reference
peak (with a 0.00001 floor). De-emphasis is then verified on the reference raw
signal, isolating it from transform rounding and cancellation in its final
state. Repeated synthesis and filtering allocate no managed storage.

SHA-256: `e2d2dbc351a64b29e0c96da57d0405ec786e5cad33ca96b75bcf3a4e87f98d21`.

`decoded-celt.bin` records 196 independently encoded Opus streams and 2,448
frames decoded through the libopus 1.6.1 public float API. All four CELT frame
sizes, four bandwidths, mono/stereo inputs and outputs, three bitrate targets,
CBR/VBR, tones, noise, impulses and silence are included. Four streams change
frame duration, bandwidth and forced coded channels while preserving decoder
state. Every final arithmetic range and sample count matches exactly. Complete
PCM has relative L2 error at most 0.00001 and peak absolute error at most 0.000003
times the larger of one and the reference peak. Frame decoding reuses budgeted
storage with no managed allocations per frame; reset is deterministic.

The little-endian file begins with `OPUSPCM1` and a 32-bit stream count. Each
stream contains 32-bit output channels, inversion-disable flag and frame count.
Each frame contains a 32-bit packet length, packet bytes including TOC, 32-bit
decoded samples per channel, unsigned 32-bit final arithmetic range and
interleaved float32 PCM. This covers CELT-only coding; SILK, hybrid coding,
packet-loss reconstruction and Ogg presentation trimming remain outside it.

SHA-256: `b21f9a0076b075e230f425f26c97012e6274f695feda80c3614e2b7252acf5bc`.

`silk-pulses.bin` records 2,880 SILK excitation pulse decodes from libopus
1.6.1. All 10/20 ms narrowband, mediumband and wideband frame lengths, three
signal types, two quantization offsets, rate levels, pulse shell splits, extra
least-significant bits and signs are covered. Constant, alternating, random,
empty and short compressed inputs exercise required range-decoder zero
extension. All signed pulses and entropy state match exactly, including the
eight parsed but discarded samples of 10 ms mediumband frames. Tests preserve
unused output and verify zero managed allocations per decode. These vectors
cover pulse decoding independently of frame side information and SILK synthesis.

The little-endian file begins with `OPUSSLP1` and a 32-bit case count. Each case
contains compressed byte count and bytes; 32-bit frame samples, signal type and
quantization offset; unsigned 32-bit arithmetic range, 32-bit whole/eighth bits;
and signed 16-bit pulses rounded up to complete 16-sample shell blocks.

SHA-256: `5f12f9206455a64f90cbe94229006c2872508fd67b3753733545191bfcbb848c`.

`silk-controls.bin` records 1,536 gain, stereo predictor and excitation cases
from libopus 1.6.1, plus all 3,970 Q7 exponent inputs from -1 through 3,968.
Independent/conditional gain coding, both subframe counts, all signal types,
quantization offsets, seeds and prior gain indices are included. Excitation
comes from the reference decoder core before prediction. Gain indices, Q16
gains, stereo Q13 coefficients, mid-only flags, Q14 excitation and entropy
state match exactly. Repeated decoding allocates no managed storage.

The little-endian file begins with `OPUSSLC1` and a 32-bit case count. Cases
contain compressed bytes, signal/conditional/subframe/history fields, gain
indices and entropy state, final gain history and Q16 gains, stereo predictors
and mid-only flag followed by entropy state, pulse count/seed/offset, signed
16-bit pulses and signed 32-bit excitation. A final 32-bit count precedes Q7
exponent/result integer pairs.

SHA-256: `65e2ad45852354277398a75fa8f8b6310775ad63666cd6dfe90d80fcbed72290`.

`silk-lsf.bin` records 768 LSF entropy decodes, 4,096 dequantizations and
1,024 stabilization cases from libopus 1.6.1. Both LPC orders, every first-stage
codebook vector, signal-type probability families, escaped residuals,
interpolation, predictor selection, inverse weights and extreme/unsorted
frequencies are covered. Indices, entropy state and all Q15 coefficients match
exactly. Repeated LSF reading and reconstruction allocate no managed storage.

The little-endian file begins with `OPUSLSF1` and a 32-bit syntax case count.
Syntax cases contain compressed bytes, order/signal/subframe fields, signed
8-bit indices, interpolation, entropy state, per-frequency model offset and
predictor, and signed 16-bit Q15 frequencies. A second count precedes direct
dequantization cases with order and indices followed by the same model/output
fields. A third count precedes stabilization cases with order, input Q15
frequencies and expected Q15 frequencies.

SHA-256: `b4d9de05997885b0b504f7929fb3eda7e3b42e7c02631ea5ddbe1b631a468e34`.

`silk-lpc.bin` records 4,096 LSF-to-LPC conversions and 2,048 inverse
prediction-gain checks from libopus 1.6.1. Both orders, every first-stage LSF
vector, extreme residuals, cosine interpolation, ordered polynomial
construction, coefficient fitting, bandwidth expansion and stability checks
are covered. All Q12 coefficients and Q30 inverse gains match exactly. Stable,
near-unit-reflection and unstable filters are included. Repeated conversion
allocates no managed storage.

The little-endian file begins with `OPUSLPC1` and a 32-bit conversion count.
Each case contains a 32-bit order, signed 16-bit Q15 frequencies, signed
16-bit Q12 output and signed 32-bit Q30 inverse gain. A second 32-bit count
precedes stability cases containing order, Q12 coefficients and inverse gain.

SHA-256: `aba5a3cc413adfe942f884f19d2836a915a5fb735a78309a5ff6177fabd6681e`.

`silk-indices.bin` records 3,456 complete SILK side-information and pulse
decodes using libopus 1.6.1 `silk_decode_indices` and `silk_decode_pulses`. All
internal sample rates, both frame durations, all conditional coding modes,
previous signal types, activity flags and changing lag histories are included.
Tests compare every frame field, gain/LSF/LTP index, entropy history and range,
then decode the excitation from that same entropy state. Voiced cases also
verify subframe pitch lags, all five Q14 taps and LTP scaling. Repeated combined
decoding allocates no managed storage; primary lag extremes clamp to the
normative per-bandwidth limits.

The little-endian file begins with `OPUSSLI1` and a 32-bit case count. Each
case contains compressed bytes; rate/subframes/coding/activity/previous signal
and lag; decoded frame and pitch fields; gain, LSF and LTP indices; resulting
entropy history and range state; signed 16-bit padded pulses and resulting
range state. Voiced cases append 32-bit subframe lags, signed 16-bit Q14 LTP
coefficients and a 32-bit Q14 LTP scale.

SHA-256: `75a9c09d64563cf58d46f5721170c8993290aa5775b403b98cf9aff79145fa42`.

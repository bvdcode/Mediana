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

`silk-parameters.bin` records 2,304 complete parameter reconstructions from
libopus 1.6.1 `silk_decode_parameters`. All internal rates, frame durations,
coding modes, first-frame resets, previous gain and LSF histories are included.
Tests compare effective interpolation, updated histories, Q16 gains, both
Q12 LPC filters, pitch lags, Q14 LTP taps and scale exactly. Repeated parameter
reconstruction allocates no managed storage and preserves unused output.

The little-endian file begins with `OPUSSLM1` and a 32-bit case count. Cases
contain rate/subframes/coding/reset/gain history and prior Q15 LSFs; frame and
pitch fields; raw gain/LSF/LTP indices; effective interpolation, final gain
history and scale; updated LSFs, Q16 gains, both compact LPC sets, pitch lags
and LTP coefficients.

SHA-256: `60d852bac2f463ddd86d37d6b54cf997189c26bb72c17a6b34fb7c12c79bb3fa`.

`silk-fixed.bin` records 12,288 normalized reciprocal and gain-ratio results
from libopus 1.6.1, covering SILK gains, LPC precision and saturation. Tests
compare the Q-format results exactly, including saturation before left shifts.
The little-endian file begins with `OPUSFIX1` and a 32-bit count. Each case
contains a 32-bit operation, denominator, precision and result. Operation
zero computes a reciprocal; operation one inserts a numerator before the
denominator and computes a ratio.

SHA-256: `f13d7b0c7317ea934ba26592cfb49412952be2e90ef996b4fc7ebfc58bd375b4`.

`silk-synthesis.bin` records 768 complete LP/LTP synthesis cases from libopus
1.6.1 `silk_decode_core`. All internal sample rates, both frame durations,
signal types, interpolated LPC filters, pitch rewhitening, gain transitions
and prior synthesis histories are covered. Tests compare every PCM sample,
LPC history word, retained output sample and final gain exactly. Repeated
synthesis uses budgeted storage with no managed allocation per frame.

The little-endian file begins with `OPUSSLS1` and a 32-bit count. Cases store
rate/subframes, signal/offset/seed/interpolation/scale; Q16 gains, two compact
Q12 LPC sets, pitch lags and Q14 LTP taps; signed pulses; prior gain, PCM and
Q14 LPC histories; expected PCM, LPC history, gain and retained PCM history.
Scalar controls and Q14/Q16 values are signed 32-bit integers; samples, pulses
and Q12/Q14 filter taps are signed 16-bit integers.

SHA-256: `b414a72f85f2d7597b7ce4f0f95928e0d35f16446150eca44f2d1b77f12362fa`.

`silk-stereo.bin` records 1,536 sequential mid/side-to-left/right frames from
libopus 1.6.1. All internal rates and frame durations are included, with
changing prediction weights, silence, impulses, uncoded side channels and
full-range samples. PCM and retained predictor/sample histories match exactly.
Unmixing runs in place without managed allocations; mono output retains the
same one-sample delay across frames.

The little-endian file begins with `OPUSSTM1` and a 32-bit count. Cases store
32-bit rate/subframes/frame index/prediction weights, both signed 16-bit input
planes, both output planes, four retained signed 16-bit samples and two final
32-bit weights. A zero frame index resets the histories.

SHA-256: `0c762ed5cf235439783d904d031ce4c0723a138e491a731ba2273f0f1f48d6d7`.

`silk-resampler.bin` records 768 sequential conversions from 8, 12 and
16 kHz to 48 kHz using libopus 1.6.1. Streams change between 1, 10 and
20 ms input blocks and include impulses, ramps, alternating full-scale
samples and noise. Every output sample matches exactly, including delay
compensation and filter history. Repeated conversions allocate no managed
storage; reset reproduces fresh-state output.

The little-endian file begins with `OPUSRSF1` and a 32-bit count. Cases
contain 32-bit input rate, frame index and input sample count, followed by
signed 16-bit input and output samples. Output length follows the rate ratio;
a zero frame index starts a new stream.

SHA-256: `d8012e62ae05ca8c3a61558b829ecbaaa12ff1828cccf1493eb3f3f62ef7bc92`.

`decoded-silk.bin.gz` records 192 streams and 3,072 packets independently
encoded and decoded with libopus 1.6.1. All three SILK bandwidths, all four
frame durations, mono/stereo input and output, FEC and coded-channel changes
are included. Signals include silence, voiced tones, noise, impulses and
chirps. Every decoded sample and final entropy range match exactly. Valid
arithmetic-coder termination includes packets whose reported whole-bit upper
bound exceeds their byte length. Bandwidth stays constant within each stream;
combined SILK/CELT transitions are outside this corpus.

The gzip member contains little-endian `OPUSSLP1` and a 32-bit stream count.
Each stream stores 32-bit internal rate, output channels and packet count.
Packets contain a 32-bit byte length, complete Opus packet, 32-bit expected
sample count and final range, then interleaved signed 16-bit reference PCM.
The source decoder's floating output is exactly representable as PCM16 for
every recorded sample. Tests compare its Float32 representation without
quantization tolerance. Repeated decoding allocates no managed storage;
budget exhaustion releases every previously allocated lease, and reset and
precancellation preserve the required state/output contracts.

SHA-256: `1c902e15b3d6c7a56c4b1bd7570d2eea93583e3044c79e4b4fba9202cf161759`.

`decoded-hybrid.bin` records 48 independently encoded streams and 576 hybrid
frames decoded through libopus 1.6.1. Both durations and bandwidths, mono/stereo
coding and outputs, three bitrate targets, tones, chirps, noise, impulses and
silence are included. SILK and CELT share one arithmetic decoder. Sample counts
and final ranges match exactly; PCM uses the same tolerance as `decoded-celt.bin`.

`decoded-transitions.bin` records 12 continuous streams and 768 frames with
switches between SILK, hybrid and CELT, narrowband through fullband, and every
mono/stereo input/output combination. Cases exercise both directions of CELT
transition redundancy, hybrid high-band fade-out and preserved SILK histories
across internal sample-rate changes. Every final range and sample count matches
exactly, with the same PCM tolerance. Repeated hybrid decoding and bandwidth
changes allocate no managed storage after workspace preparation.

Both files use the `decoded-celt.bin` layout, with magic `OPUSHYB1` and
`OPUSTRN1` respectively.

decoded-hybrid.bin SHA-256: `20c8cd4bfbaadc75a52efaebe56b7b9e2a88ed6380401e6cda86ed30930b9d7b`.

decoded-transitions.bin SHA-256: `77f011ba1ed4bf35ae084f8570a3fdb17135625f2f85abd4c4fcc66256a7dfe0`.

`decoded-multistream.bin` records 12 libopus 1.6.1 streams and 144 packets.
The standard one-through-eight-channel Vorbis layouts and discrete mappings
include duplicated, unreferenced and silent channels. Packets vary from 2.5 to
60 ms, including multiple CELT frames and self-delimited substreams. The public
factory's complete mapped float32 PCM uses the `decoded-celt.bin` tolerance.
Each stream's header records output channels, family, stream/coupled counts,
channel map and packet count. Packets record length, encoded bytes, sample count
and interleaved float32 PCM. The file starts with `OPUSMST1` and a stream count;
scalar fields are little-endian 32-bit integers, channel maps are bytes.

`ogg-stereo.f32le` is libopus 1.6.1 float32 output for the Ogg container corpus,
with 312 leading samples and final granule padding removed. It contains 65,760
stereo samples per channel. `ogg-seek.bin` records five independent decoder
restarts, each using exactly four preceding 20 ms packets, clamped at the
original beginning. It starts with `OPUSSEK1` and a 32-bit count; entries record
32-bit output packet, first preroll packet, presentation sample and sample count,
followed by interleaved float32 PCM. Reverse and repeated public seeks compare
timestamps, sample counts and PCM with this equally primed independent decoder.
The beginning applies pre-skip; midstream restarts do not.

decoded-multistream.bin SHA-256: `69d7757398a96b1376d45e8117fcdef7d9a600f640f5c4405c8aae4aba60efb3`.

ogg-stereo.f32le SHA-256: `29a251245580a03c8f1bef08c1f105ba1f2b5e5edf9eef406b3892ad837d56d9`.

ogg-seek.bin SHA-256: `c5761a62315f4c760f9304bc7f5f97bbe20026b9d7d6dd2c9e5509f15bab337b`.

`silk-signal.bin` records 4,096 signal-energy, square-root and Q16 chirp
filter-expansion cases from libopus 1.6.1. Signal lengths span 1 through 320,
including silence, noise and saturated samples. Both LPC orders, signed integer
boundaries and chirps near unity are included. All fixed-point results match
exactly. The little-endian file starts with `OPUSSIG1` and a 32-bit count. Cases
contain square-root input/output, sample count and signed 16-bit PCM, 32-bit
energy/shift/order/chirp and signed 16-bit input/output LPC coefficients.

SHA-256: `ecb99a4e5ebd445cb7aa846b543850f7512b0c962475bc0ef83e16511c69ea9b`.

`silk-concealment.bin` records 288 sequential lost-frame reconstructions from
libopus 1.6.1 across all SILK internal rates, both frame sizes, signal types,
pitch filters, gains and prior synthesis histories. PCM, LPC/output histories,
attenuated filters, drifting pitch and random state match exactly. Repeated
reconstruction allocates no managed storage. Comfort noise and reconnection to
new received packets are checked separately.

The little-endian file starts with `OPUSSLC1` and a 32-bit case count. Cases
record rate/subframes/signal/pattern, gains, LPC coefficients, pitch lags, LTP
coefficients and scale, excitation, output history and LPC history. Four output
frames follow with PCM, LPC/output histories, pitch, random scale/seed and
attenuated LTP/LPC coefficients. Samples and filter coefficients are signed
16-bit values; remaining scalar and excitation/history fields are 32-bit.

SHA-256: `5bfab1b30ef849d5ed9b7326c591254dae8eb05d393edf0ee5f81530132f3f5b`.

`silk-noise.bin` records 192 sequential comfort-noise frames from libopus
1.6.1, preceded by eight inactive parameter updates per stream. All internal
rates, both frame sizes and eight gain ranges cover both noise-energy scaling
branches. Saturated input PCM, noise synthesis histories and random state match
exactly. The file starts with `OPUSCNG1` and a 32-bit case count. Cases contain
32-bit rate/subframes/level, then eight sets of signed 16-bit LSFs and 32-bit
gains/excitation. Four lost frames contain 32-bit random scale and previous gain,
signed 16-bit input/output PCM, 16 signed 32-bit synthesis words and a 32-bit seed.

SHA-256: `cbb60c4d3f7ff7c22458e9a061629276b7d4f00dc86e075af8e9fa68b6d8a739`.

`decoded-silk-recovery.bin.gz` records 4,608 packets from 48 libopus 1.6.1
streams. SILK rates 8/12/16 kHz, mono/stereo decoding, coded channel changes
and 10/20/40/60 ms packets include received speech, paired packet losses,
long silent intervals with DTX and resumed speech. All PCM samples and final
entropy ranges match exactly. Each stream records 32-bit rate/output channels/
packet count. Packets record 32-bit encoded length (zero for loss), bytes,
32-bit decoded sample count/final range and interleaved signed 16-bit PCM.
The decompressed header is `OPUSREC1` followed by a 32-bit stream count.

SHA-256: `e7517e3d76ecaafe108e85bfd26dc64490a52e4fb1e404d81d8bf730b3a744b9`.

`celt-prediction.bin` records 128 mono/stereo histories from libopus 1.6.1.
Noise, periodic mixtures, transients, impulses, silence and very small signals
exercise the two-stage pitch search, 24-tap windowed LPC analysis, residual
filtering and synthesis. Pitch lags match exactly; float values use a relative
comparison of 1e-5 with a unit floor. The file starts with `OPUSCLP1`, a 32-bit
case count and 120 float32 analysis-window coefficients. Each case records a
32-bit channel count, 2,048 float32 samples per channel and a 32-bit pitch lag.
Per-channel data then contains 25 autocorrelations, 24 LPC coefficients,
512 residuals and 512 reconstructed samples, all little-endian float32.

SHA-256: `a01061720d5b35701fa4335d078f0fa33a8b1cf349abe0c7dbc3f72a43339937`.

`celt-concealment.bin`

Six paired-loss states from an independently decoded narrowband CELT stream
record pitch, 24 LPC coefficients, 2,048 history samples and 120 overlap samples
before and after reconstruction. Folded overlap is checked independently before
the following transform. The header is `OPUSPLP1` and a 32-bit case count.
Cases contain a 32-bit frame size, period, float32 gain, 32-bit tapset/pitch/
previous-periodic flag, 2,168 float32 input samples and 24 LPC coefficients,
then the corresponding output samples/coefficients, 32-bit resulting pitch
and 60 float32 folded-overlap values.

SHA-256: `f77eaa5a458c88969536c419dcfa0f6f2f9e1290251c9a77e5c5c6cfe0dde61f`.

`decoded-celt-recovery.bin.gz`

64 libopus 1.6.1 streams contain 6,144 CELT packets across all bandwidths,
frame sizes, coded channel changes and mono/stereo outputs. Received speech,
short loss bursts, up to 50 consecutive lost packets, DTX and resumption
exercise pitch-based reconstruction, noise synthesis and safe energy prediction.
The decompressed header is `OPUSCRC1` and a 32-bit stream count. Streams contain
32-bit bandwidth/output channels/packet count; packets contain a 32-bit length,
encoded bytes (zero length for loss), 32-bit sample count/final range and
interleaved float32 PCM.

Recovery comparisons require at least 40 dB signal-to-error ratio per complete
stream and peak error below 0.02 times the larger of one and the reference peak.
Loss reconstruction is non-normative and is sensitive to rounding in retained
histories and LPC analysis. Scalar and SIMD libopus builds also differ on this
corpus. The stricter ordinary-frame comparisons remain unchanged.

SHA-256: `5a14c7c3b19c54eddce8fe4459a69f7ef3771480f1274e1cfdfbd2519054f07c`.

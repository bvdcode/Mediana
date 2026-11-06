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

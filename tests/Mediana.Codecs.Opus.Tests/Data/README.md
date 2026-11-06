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

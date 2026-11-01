# Matroska EBML foundation

`Mediana.Containers.Matroska` contains internal EBML value decoding and bounded
random-access element-header reading. Track descriptions, packet reading,
seeking and container attachment extraction are the next integration work.
MKV probing and decoding are not exposed through the public engine yet.

## Format rules

The implementation follows [RFC 8794](https://www.rfc-editor.org/rfc/rfc8794),
with the Element ID update in [RFC 9559, section 4.2](https://www.rfc-editor.org/rfc/rfc9559#section-4.2).
Matroska's one-byte ID `0x80` is valid. IDs retain their marker, use the shortest
valid encoding and reject reserved all-one values. Data sizes accept padded
encodings and represent the all-one sentinel as an unknown extent. Schema-level
parsing must resolve where an unknown-sized master ends.

Unsigned and signed values accept zero through eight bytes. Empty numeric
values use an explicit schema default supplied by the caller. Four- and
eight-byte IEEE floating-point values preserve signed zero and nonfinite values;
field-specific range restrictions belong to the corresponding schema reader.
String values require printable ASCII or valid UTF-8 before their first null
terminator. Following bytes are ignored as specified in RFC 8794, section 13.

## Bounds and ownership

The element reader borrows a random-access source and leaves its sequential
cursor untouched. Its reusable 12-byte header buffer is charged to the common
media allocator and released on disposal. It reads within the supplied parent
boundary and known input length, validates fragmented read counts and checks
header and payload extents before constructing an element. A large declared
payload does not cause a payload allocation.

The buffer quota covers media bytes; object and asynchronous I/O overhead are
separate. Header reading does not inspect an entire payload or establish its
codec validity. Operations and disposal are sequential.

## Verification

The tests cover all eight VINT widths, reserved/canonical ID boundaries, padded
sizes, unknown extents, 2,048 signed/unsigned input patterns and normative string
termination. Reader tests cover every incomplete prefix of a maximum-width
header, nested boundaries, offsets at the address limit, oversized declarations,
invalid source counts, cancellation during an asynchronous read, reuse, quota
rejection and independent source lifetime.

All 2,734 managed tests and the complete strict Linux x64 Native AOT runner pass.
The Release build, formatting and license-header checks also pass.

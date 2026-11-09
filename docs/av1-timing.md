# AV1 timing syntax

The AV1 decoder reads sequence timing information with or without a decoder model. Clock values retain their full unsigned 32-bit range. Equal-picture-interval cadence is represented exactly with the existing rational `FrameRate` type; an unspecified interval does not imply a constant frame rate.

The decoder model includes the signaled clock, field lengths, operating-point buffer delays and initial display delays. Frame parsing consumes presentation times for displayed coded pictures and `show_existing_frame`, and buffer-removal times for each applicable operating point. The current base-layer decoder selects operating point zero, which must include temporal/spatial layer zero. This does not implement scalable-layer selection or decoder scheduling deadlines.

Packet timestamps and durations supplied by the container remain authoritative. Decoding does not synthesize packet timestamps from sequence timing or replace container times with decoder-model counter values. Codec timing values are retained in parsed headers; this change does not add an AV1-specific field to the general video format API.

The syntax and range checks follow the [approved AV1 specification, version 1.0.0 with Errata 1](https://aomediacodec.github.io/av1-spec/av1-spec.pdf), sections 4.10.3, 5.5.1–5.5.5, 5.9.2, 5.9.31 and 6.4.3–6.4.5. Zero-valued clock terms, truncated fields and the reserved maximum UVLC picture interval are rejected.

## Independent reconstruction fixtures

`timingEqual.obu` and `timingModel.obu` are six-picture 48×40 Main-profile 8-bit 4:2:0 sequences encoded with libaom 3.8.2 through FFmpeg 6.1.1. Their source is `testsrc2` at 30000/1001 pictures per second. Encoding uses one thread, quality 30, `cpu-used=8` and these common options:

```text
enable-keyframe-filtering=0:enable-global-motion=0:enable-restoration=0:enable-cdef=0:enable-interintra-comp=0
```

The equal-interval sequence uses `timing-info=1`; the decoder-model sequence uses `timing-info=2`. The latter contains seven coded pictures and one reference display, including hidden reference pictures. FFmpeg's independent `trace_headers` parser supplies the expected raw timing fields. Tests compare those signaled values rather than deriving them from picture ordinals.

`timingVariable.obu` changes only the equal sequence's sequence-header metadata: `equal_picture_interval` becomes zero and its now-absent UVLC interval is removed, with OBU size and trailing alignment updated. It retains timing information without a decoder model or constant cadence. Its compressed tile data is unchanged.

All three sequences are decoded independently with libdav1d 1.4.1. The common `timing.reference.yuv` contains all 17,280 visible Y/U/V samples from six pictures; every sample must match. The public-factory test repeats each complete sequence after reset, supplies deliberately different packet timestamps/durations, checks random-access decisions and requires all allocator leases to be released. Unsigned-field and UVLC tests separately exercise full-range clock products, sentinel termination and multiple operating-point removal times.

| Fixture | SHA-256 |
| --- | --- |
| `timingEqual.obu` | `8e327c848279ca2d8066a257b6d9bba4274c1547c94654cc5c7b75febaedeae3` |
| `timingModel.obu` | `9a461295f160409fc87ac2b7d1192833db3b89e8ebd3c63588759951c8ea5901` |
| `timingVariable.obu` | `c2c4875672b621094422232b241c458268b3191462f10aa144ef7b20be0b9aa9` |
| `timing.reference.yuv` | `7ee05a986b7f0bd9f6b771d5d75bb19a2e5bf05edbee6e2bc43c99f07bdaf9f4` |

These fixtures establish this timing/header path and the associated reconstruction cases; they do not establish support for every AV1 coding tool, operating point or layer.

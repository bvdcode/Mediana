# QOI images

`Mediana.Codecs.Qoi.QoiImageDecoder` implements the common synchronous and
asynchronous image decoder and metadata interfaces. It is registered in the CLI
and the default embedded-image registry.

QOI 1.0 RGB and RGBA images support every opcode: RGB, RGBA, index, difference,
luma and run. Channel differences wrap modulo 256. Straight alpha, including
RGB beneath zero alpha, is retained exactly. The header's sRGB/linear transfer
declaration is preserved. Channel count is informational and does not change
opcode interpretation.

Inspection returns dimensions, channel count and color-space declaration without
decoding pixels. Decoding allocates one owned RGBA image against the supplied
budget and uses a fixed 64-entry index on the stack. Source-size limits and
cancellation come from the common request. Invalid dimensions, declarations,
end markers, repeated index opcodes, truncated chunks, oversized runs and
trailing payload are rejected.

Tests cover every opcode, zero-initialized index entries, arithmetic wraparound,
strict malformed-input handling, cancellation, allocation cleanup, registry
selection and an independent Pillow 12.3 QOI encoding compared byte-for-byte.

Reference: [QOI specification 1.0](https://qoiformat.org/qoi-specification.pdf).

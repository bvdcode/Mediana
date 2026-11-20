# Fragmented MP4 input

`Mp4Reader` reads ordinary sample tables and movie fragments through the same packet, metadata and indexed-seeking APIs. `MediaEngine` uses that reader for audio and video decoding; clients do not select a separate fragmented-file pipeline.

The input is a self-contained file with a known length and random-access reads. An initialization-only file exposes its declarations and empty packet indices. Opening reads `moov`, `moof` and top-level box headers. It skips encoded `mdat` payloads; MPEG audio configuration inspection retains its existing first-frame-header read. Fragment metadata shares one owned, bounded buffer with movie metadata. Sparse sample checkpoints support random access without storing a separate object for every packet.

## Timing and locations

- `trex` supplies track defaults, overridden by `tfhd` and individual `trun` fields.
- Absolute, movie-relative and inherited data offsets are supported, including signed run offsets and multiple runs per track fragment.
- `tfdt` versions 0 and 1 supply decode times. Without `tfdt`, decoding continues from the previous fragment or initial sample table. An advancing decode time extends the preceding sample as specified by ISO BMFF; overlapping decode timelines fail.
- `trun` version 0 composition offsets are unsigned; version 1 offsets are signed. Container timestamps retain the selected media or movie time base.
- Ordinary samples in `moov` can precede later fragments. Sync flags and recovery groups use one continuous sample-number space.
- Zero-duration media edits in a fragmented movie translate timestamps through subsequent fragments; they do not discard the presentation. Original declared movie/track durations remain separate from complete indexed packet totals.

Seeking validates codec restart requirements and includes declared `roll`/`prol` history across fragment boundaries. Track-level group indices and fragment-local indices starting at `0x10001` resolve against their respective declarations. See [audio seeking](audio-seeking.md) and [MP4 recovery groups](mp4-recovery.md).

`MaxMetadataBytes` applies to the combined movie and fragment headers. `MaxSamplesPerTrack` applies to the complete track. Cancellation and malformed metadata release allocated buffers without taking ownership of the source. A failed packet read does not advance the sequential position.

## Validation

Independent FFmpeg remuxes cover relative, absolute and inherited addressing, signed composition offsets and mixed ordinary/fragmented samples. Every packet is checked against independent payload hashes and timestamps. Video checks compare every decoded pixel and repeat indexed seeks. AAC checks compare independent Float32 PCM, exact deterministic restarts and per-channel block energy for noise substitution.

A 30-minute H.264/AAC file contains 9,002 video and 85,499 audio packets. Opening reads 1,057,953 bytes of headers and metadata; complete compressed-payload hashes for both tracks and the complete decoded video hash match FFmpeg. Two hundred random packet requests read only 53,718 additional bytes. One hundred video seeks reproduce sequential pixels and timestamps. A separate 30-minute non-noise-substitution AAC track passes 100 byte-exact PCM restarts. Noise-substitution restarts preserve timestamps and block energy; their random phase is not required to equal uninterrupted decoding.

A standalone .NET 11 Native AOT client passes these complete long-file and repeated-seek checks. It also checks all five independent video fixtures against the same pixel references.

The long-file checks exercise index size and timeline continuity using small frames. They are not HD codec-throughput comparisons. Growing/non-seekable input and loading a separate initialization resource are separate capabilities.

References: [ISO/IEC 14496-12](https://www.mpeg.org/standards/MPEG-4/12/), clauses 8.6.6, 8.8 and 8.9; [W3C ISO BMFF byte-stream format](https://www.w3.org/TR/mse-byte-stream-format-isobmff/). Fixture construction and reference timing translations are documented in [the test data](../tests/Mediana.Tests/Data/Mp4/fragment-input.md).

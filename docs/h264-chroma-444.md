# H.264 4:4:4 decoding

`H264DecoderFactory` decodes 8-bit and 10-bit 4:4:4 I/P/B pictures through the
same video-session API as 4:2:0 and 4:2:2. CABAC, CAVLC, intra 4×4/8×8/16×16,
inter 4×4/8×8 transforms, cropped dimensions, multiple slices and MBAFF use the
shared reconstruction pipeline with flat scaling lists.

Y/Cb/Cr output uses `VideoPixelFormat.Yuv444P8` or `Yuv444P10`. Matrix coefficient
zero selects RGB sample interpretation and G/B/R plane order (`GbrP8` or
`GbrP10`). Ten-bit samples occupy the low bits of native-endian 16-bit words.
Each plane has the full coded dimensions. Published frames retain the decoded
picture allocation without a second image-sized copy.

Full-resolution color planes reuse luma spatial prediction and quarter-sample
six-tap motion interpolation. Their residuals use independent coefficient counts,
DC flags and CABAC states. H.264 tables 9-25 through 9-33 repeat initialization
pairs from the existing contexts; the initialized states are copied and then
adapt independently. Filtering reuses the corresponding luma boundary strength
and applies the full-resolution filter to all three planes.

## Independent verification

Thirteen libx264 MP4 sequences cover 8/10-bit samples, CABAC/CAVLC, I/P/B
pictures, 4×4 and 8×8 transforms, nonzero chroma QP offsets, QP26/45, two slices,
two GOPs, RGB planes and MBAFF. Visible dimensions are 83×75 or 83×76, exercising
cropped coded borders. Every sample of all 234 frames and 234 reverse seeks
matches FFmpeg's independently decoded reference exactly. Checks also cover
presentation timestamps, durations, EOF, output ownership across session disposal
and release of all allocator buffers.

The same sequences and pixel checks run in the Linux x64 Native AOT test
executable. RGB publication additionally checks sRGB channel order and the
absence of chroma-siting metadata on RGB planes.

The fixture construction and reference hashes are recorded in
[the fixture notes](../tests/Mediana.Tests/Data/Mp4/chroma-444.md).
Separate color planes, custom scaling lists and transform bypass are separate
reconstruction features and remain unsupported.

## Existing 4:2:0 decoding cost

A paired comparison decodes and hashes all 90 frames of two 30 fps CABAC
sequences. Both versions use the same .NET 11 runtime, dependencies and test
program; only the H.264 decoder assembly changes. Runs alternate version order
in fresh processes pinned to one CPU, with tiered compilation disabled. The
table gives medians of five measured runs after discarding the first pair.
Decoded pixel and timestamp hashes match between versions.

| Input | CPU before / after | Elapsed before / after | Peak RSS before / after | Managed allocations before / after |
| --- | --- | --- | --- | --- |
| 640×360 | 668 / 677 ms | 679 / 693 ms | 52.0 / 52.1 MiB | 5.60 / 5.67 MiB |
| 1280×720 | 1473 / 1444 ms | 1488 / 1462 ms | 62.9 / 62.8 MiB | 22.14 / 22.21 MiB |

CABAC decoding states occupy one array indexed by the standard's context
numbers. This avoids compact-table remapping for each arithmetic bin. The
4:4:4 extension does not materially change peak memory or CPU on these existing
4:2:0 inputs. These short, single-worker runs do not characterize long files,
parallel throughput or 4:4:4 decoding speed.

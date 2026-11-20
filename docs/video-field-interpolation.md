# Field interpolation

`VideoFieldInterpolator.Apply(field, frameHeight, allocator)` reconstructs a
progressive surface from one explicitly selected field. Use `VideoFrame.GetField`
for an interleaved source. The full output height is required: a single field
does not determine whether the original frame height was odd or even.

`VideoFormat.FullFrameHeight` preserves this geometry when it is known. Frame
surfaces use their visible height; standalone fields created without this
information leave it null. `GetField` carries the source's full height, and the
H.264 decoder supplies it from the sequence's visible dimensions. Color
conversion and permitted horizontal resizing preserve it. An explicit
interpolation height must agree with the declared full height.

Existing field rows remain exact. Missing rows use linear interpolation within
the same field; edges extend the nearest row. Integer averages round upward
without overflow. Straight RGBA uses alpha-weighted colors, while premultiplied
floating RGBA filters its stored components. Planar YUV/GBR supports 8–16-bit
samples. The selected field's chroma position becomes the progressive position.
No samples from the other temporal field are mixed into the result.

The operation borrows the input, allocates one output surface through the given
allocator and preserves source timing. It does not split a frame duration or
schedule multiple output frames. The caller assigns field timing. Display hints
become one frame display with the original source-scan indication.

This spatial interpolation does not recover missing vertical detail or use
neighbouring pictures for motion-adaptive reconstruction.

## Validation

The 52 field, scan and presentation tests pass in Release, including 13 field
interpolation tests. The latter also pass with hardware intrinsics disabled.
Coverage includes all 65,536 pairs of 8-bit values, complementary 16-bit values,
odd sizes, padded rows, independent field chroma locations, alpha, large floating
values, cancellation, memory limits and independent disposal.

## Scalar and vector comparison

.NET 11 Native AOT, one fixed core, elevated process priority, shared pooled
buffers, three runs of each implementation with alternating order. Source
construction and two warmup frames precede timing. Measurements cover complete
field reconstruction, output allocation and disposal. Hashes and consumed output
values agree across all runs, and the live allocator returns to its input usage.
`System.Numerics.Vector<T>` accelerates integer row averages; this API predates
.NET 11. No external dependency is required.

| Output | Frames/run | CPU median, scalar → vector | Wall median, scalar → vector | Peak RSS, scalar → vector | Allocated/run, both |
| --- | ---: | ---: | ---: | ---: | ---: |
| 1280×720, 8 bit | 6000 | 8.001 → 1.135 s | 8.012 → 1.136 s | 19.08 → 19.09 MiB | 3.626 MiB |
| 1920×1080, 10 bit | 2400 | 6.522 → 2.489 s | 6.523 → 2.490 s | 26.24 → 26.23 MiB | 1.457 MiB |
| 3840×2160, 8 bit | 800 | 9.985 → 2.023 s | 9.986 → 2.023 s | 37.21 → 37.15 MiB | 0.492 MiB |

Scalar CPU ranges are 6.617–8.939 s, 5.857–7.106 s and 9.268–10.128 s;
vector ranges are 1.098–1.159 s, 2.442–2.571 s and 1.975–2.123 s. The ranges
do not overlap, but the scalar variation limits the precision of a speed ratio.
These measurements establish gains for field reconstruction only, not for
complete decoding, transcoding or motion-adaptive deinterlacing.

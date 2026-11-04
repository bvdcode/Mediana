# H.264 fractional interpolation reuse

A complete 180.125-second High-profile CABAC input at 1280x720 and 24 fps
was decoded to 4,323 YUV420P8 pictures. Every output byte, rational timestamp,
duration, EOF and released media allocation was checked. The decoded payload
is 5,976,115,200 bytes with SHA256
`8a2f459cc105b3a2a0f6239c5be0e0341bd4551317812544bf2fadcc5649a627`.

The horizontal, vertical and diagonal half-sample filters follow
[H.264 section 8.4.2.2.1](https://www.itu.int/rec/T-REC-H.264-202606-I).
Coverage is counted separately for each physical reference picture, including
the clipped six-tap border coordinates. Counts below include all requests over
the complete decode; they are not simultaneous memory requirements.

| Filter | Requested samples | Distinct samples | Samples in touched 16x16 tiles |
| --- | ---: | ---: | ---: |
| Horizontal | 1,110,128,640 | 642,646,518 | 831,641,331 |
| Vertical | 1,441,626,432 | 819,616,788 | 1,030,081,443 |
| Diagonal | 728,616,384 | 466,097,763 | 626,594,697 |
| Total | 3,280,371,456 | 1,928,361,069 | 2,488,317,471 |

Of the requested samples, 41.22% repeat an already computed coordinate in the
same reference picture. Computing complete touched 16x16 tiles would reduce
filtered output samples by 24.15%, because partially used tiles also compute
unused samples. Diagonal filtering additionally prepares 970,861,344 unrounded
horizontal intermediate samples in the original block calculation.

The estimated peak data capacity is 10.66 MiB for complete three-plane storage
or 8.38 MiB for separately stored touched tiles. These estimates exclude cache
metadata and process overhead. Lazy plane allocation reaches the same peak as
three-plane allocation on this input. Estimated capacity is released when its
reference picture leaves the decoded picture buffer, even if output pixel
leases remain alive.

Independent checks compare packed coverage with individual scalar coordinates
for 32,768 rectangles, including all fractional positions and extreme motion
coordinates. Another 6,144 filtered samples compare the implementation and
canonical border coordinates with scalar six-tap convolution. Both checks pass
in managed, software and Native AOT execution. Estimated live cache capacity
and owned media storage are zero after the complete decode.

These counts quantify repeated work and possible cache data capacity. Actual
processing cost also includes tile creation, lookup, sample copying and cache
allocation; CPU and resident memory require a complete before/after comparison.

## Budgeted reference-picture cache comparison

A cache prototype lazily computes 16x16 tiles inside three addressable filtered
planes. It uses immutable published reference pixels. All plane data and tile
flags use the shared media allocator and are released with their reference
picture. Unpublished mutable encoding references retain the original calculation.

Independent scalar convolution checks pass for 7,392 original and 14,784 cached
prediction blocks in managed, software and Native AOT execution. The checks
cover every fractional position, borders, extreme motion vectors, repeated
requests and output guards. Reference release, retained output leases and a
failed cache allocation also preserve the shared memory budget. Another 729
H.264 tests pass in each managed mode.

Four complete exact decodes use Native AOT, one decoder worker and one logical
CPU in original/cache/cache/original order. The host is shared. Values below
are medians of the two complete operations per implementation.

| Metric | Original | Cached |
| --- | ---: | ---: |
| CPU, user + system seconds | 48.058 | 56.911 |
| Elapsed seconds | 76.834 | 100.224 |
| Peak resident memory, MiB | 69.65 | 137.25 |
| Cumulative allocated bytes | 10,643,457,036 | 16,706,278,440 |

CPU increases by 18.42%. This organization fails the required CPU improvement,
so it is not adopted. The reverse order and secondary CAVLC/encoding controls
are not run after that failure. Additional memory is an accepted tradeoff when
it improves processing speed; this prototype does not establish that benefit.
These results do not change the last independent FFmpeg comparison.

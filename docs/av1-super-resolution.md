AV1 super-resolution follows the frame-size, upscaling and restoration-unit
processes in the
[AV1 specification, version 1.0.0 with Errata 1](https://aomediacodec.github.io/av1-spec/av1-spec.pdf).

The decoder distinguishes coded geometry from the upscaled pixel geometry.
Block contexts, segmentation maps, motion fields, deblocking and CDEF retain the
coded dimensions. Output layouts, stored reference samples, motion compensation,
render aspect ratios and film grain use the upscaled dimensions. A subsequent
frame can inherit the upscaled reference size and select a different denominator.

Upscaling runs after CDEF and before loop restoration. Restoration units map
coded tile columns to the upscaled plane. Only the four retained deblocked rows
around each restoration stripe boundary are upscaled separately; no unused
full-frame pre-CDEF copy is retained. A temporary coded frame and the final
upscaled frame use distinct allocator-owned storage. Allocation failure,
cancellation, reset and disposal release their leases independently of returned
frames. Pictures without super-resolution retain the existing storage path.

The shared row kernels support byte and unsigned sixteen-bit storage. All 720
independent AOM 3.8.2 convolution cases match, covering 2,573,376 samples at eight,
ten and twelve bits, every denominator, padded edges and widths from 1 to 65,535.
High-depth row-kernel coverage does not establish complete high-depth decoding.

Thirteen AOM 3.8.2 Main-profile eight-bit sequences cover fixed denominators
9–16, changing denominators including an unscaled frame, 193×137 odd dimensions,
65×73 monochrome, two tile columns, active restoration and film grain. Every
visible plane matches independent dav1d output: 156 pictures, checked through
two public decode/reset passes. Held output frames remain unchanged while later
reference frames change scale; scoped-budget failures release intermediate
storage. Normal, scalar and strict Native AOT execution match the independent
reference, and the combined Native test consumer completes successfully.

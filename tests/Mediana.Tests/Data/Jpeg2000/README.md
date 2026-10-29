# JPEG 2000 reference images

These 70 original synthetic fixtures originate from Render.NET commit
03cb502, under the MIT license, copyright 2026 Vadim Belov.
The original encoded files, integer reference samples and manifest hashes are preserved.

Reference samples were decoded independently using OpenJPEG 2.5.4. Reversible
cases require exact equality; the three irreversible/modified-quantization cases
allow the per-fixture maximum error of one level. Samples retain their native
precision and signedness. This is regression coverage, not complete conformance.

Coverage includes gray/RGB/RGBA, 16-bit and signed samples, subsampling, tile and
image origins, all progression orders, precincts, quality layers, tile parts,
ROI maxshift, SOP/EPH and code-block style combinations. Native comparison tools
are not runtime dependencies.

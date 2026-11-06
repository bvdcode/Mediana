# HEIC reference images

These 42 original synthetic files originate from Heic.NET commit `3eefb9f`,
under the MIT license, copyright 2026 Vadim Belov. The encoded files and
reference hashes are unchanged. The manifest records visible dimensions,
input hashes, complete RGBA hashes and decoder pixel limits.

The source patterns were encoded with x265. FFmpeg independently reconstructed
the elementary HEVC pictures; separate integer color conversion produced the
RGBA references. The containers also opened with libheif 1.23.2.

Cases cover 8/10-bit and monochrome intra pictures, deblocking, SAO, scaling
lists, rotations, mirrors, clean aperture, identity images, grids and alpha.
These checks establish the supported still-image subset, not general HEVC video
or full HEIF conformance. No native codec is needed to run the tests.

`large-grid.heic` is an additional original synthetic CLI regression input,
outside the 42-entry manifest. Its 4608×4096 grid contains 72 identical 512×512
8-bit HEVC tiles. The gray source was encoded with FFmpeg's libx265 encoder,
using one intra frame, disabled wavefront processing and one encoding thread.
The grid uses distinct item identifiers referencing the same encoded extent.
libheif independently decoded every RGBA pixel as `(129, 129, 129, 255)`;
the CLI comparison allows one RGB level for integer color-conversion rounding.

Six additional original ICC integration fixtures retain the existing synthetic
HEVC payloads and NCLX properties. They add a `colr` property and its item
association using procedural profiles from `../Icc`:

| HEIC fixture | ICC profile | Association |
| --- | --- | --- |
| `icc-prof-basic-8bit.heic` | `sampled-v2.icc` | Coded primary |
| `icc-ricc-basic-8bit.heic` | `linear-v2.icc` | Coded primary |
| `icc-prof-grid.heic` | `distinct-trc-p3-v4.icc` | Derived grid |
| `icc-ricc-alpha-rotated.heic` | `parametric-4-v4.icc` | Rotated color primary with auxiliary alpha |
| `icc-prof-identity.heic` | `gamma22-v2.icc` | Coded child of the identity primary |
| `icc-prof-monochrome.heic` | `gray-gamma22-v4.icc` | Monochrome primary |

These files are outside the 42-entry manifest. Checks compare the original
decoded samples, retained profile bytes and `prof`/`rICC` identity, then exercise
common sRGB conversion, fitted previews, copied/direct frame ownership and CLI
PNG export. They use the same MIT license and copyright as the source fixtures.

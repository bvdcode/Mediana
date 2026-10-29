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

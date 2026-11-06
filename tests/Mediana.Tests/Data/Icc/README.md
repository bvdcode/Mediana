# ICC color conversion references

Ten procedural ICC v2/v4 profiles exercise RGB and grayscale matrix/TRC conversion:
identity and single-gamma curves, 1024-entry sampled curves, all five parametric
curve functions, distinct channel curves and wide-gamut matrix colorants. Shared
tag extents exercise profile table aliasing. RGB cubes include 4913 colors and
varying alpha; the grayscale fixture includes all 256 sample values.

Reference RGBA bytes were generated with Pillow's LittleCMS bindings using
relative colorimetric intent, an sRGB destination and no black-point compensation.
Alpha must match exactly; RGB permits at most two 8-bit levels of difference for
fixed-point profile, interpolation and destination-profile rounding.

The profiles and samples are procedural, with no photographs or document content.
No runtime dependency on the reference software is required.

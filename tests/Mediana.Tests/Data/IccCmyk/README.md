# CMYK ICC references

Twelve procedural ICC v2/v4 input profiles cover four-channel AToB transforms:

- `mft1`: 8-bit Lab CLUT and 256-entry input/output tables.
- `mft2`: 16-bit Lab or XYZ CLUT and 257-entry input/output tables. Lab uses
  the legacy 65280 encoding scale in both profile versions.
- `mAB`: 8/16-bit Lab or XYZ CLUT, nonuniform 4×5×6×7 grids, input and output
  curves, nonidentity M curves and affine matrix. A separate profile shares
  complete curves between the A and B groups and omits M/matrix.
- `v2-a2b0-only`: relative colorimetric conversion through the default AToB tag
  when no intent-specific tag exists.

`samples.cmyk` contains a 64×64 CMYK8 image: the complete five-level four-channel
cube followed by deterministic random ink values. Zero means no ink; 255 means
full ink coverage. There is no alpha channel in this file. The profiles and
source pixels are procedural; no third-party ICC profiles are included.

The matching `.rgba` files were produced with Pillow 12.3.0/LittleCMS 2.19, an sRGB output
profile, relative colorimetric intent, `NOOPTIMIZE` and no black-point
compensation. Output alpha is always 255. Transform tests allow at most two
8-bit RGB levels of numerical difference and require exact alpha. They also
check fitted preview equivalence, profile ownership and complete buffer release.

Given a profile and `samples.cmyk`, references can be regenerated with:

```python
from PIL import Image, ImageCms

image = Image.frombytes("CMYK", (64, 64), open("samples.cmyk", "rb").read())
transform = ImageCms.buildTransform(
    "v4-mab2-lab.icc", ImageCms.createProfile("sRGB"), "CMYK", "RGB",
    renderingIntent=1, flags=ImageCms.Flags.NOOPTIMIZE)
pixels = ImageCms.applyTransform(image, transform).convert("RGBA").tobytes()
```

The `cmyk-*` and `ycck-*` fixtures reference the existing 37×29 JPEG corpus in
`Mediana.Codecs.Raster.Tests/Data`. Their `.cmyk` files contain libjpeg-turbo's
independently decoded ink samples; plain CMYK complements Pillow's channel
convention as described in that corpus. Each is converted independently with
`v2-mft2-lab` and `v4-mab2-xyz` to produce the corresponding RGBA reference.
JPEG tests allow two levels in ink samples. End-to-end JPEG plus ICC tests allow
ten RGB levels and mean absolute error no greater than 0.1 across all bytes:
one level of IDCT rounding can be amplified near the sRGB gamut boundary.
The isolated ICC tests retain their two-level bound on identical inputs.

The source JPEGs are reused without duplicating compressed image bytes. Tests
insert two ICC APP2 fragments in reverse physical order and release the encoded
input before color conversion. Sequential, progressive, plain CMYK, Adobe CMYK
and YCCK reconstruction run in managed and Native AOT checks.

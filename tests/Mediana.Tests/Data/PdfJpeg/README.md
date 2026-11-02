# JPEG image XObjects

The 57 PDFs embed the 47 independent baseline, extended and progressive images
from [the JPEG corpus](../Jpeg/README.md), nine PDF color and mask cases and
an independent 512-square JPEG for whole-image allocation checks.
Each PDF has one image placement at its original dimensions. Additional cases
exercise APP14 precedence, `ColorTransform 0`, a preceding ASCIIHex filter,
ignored application presentation metadata, inverted Decode ranges, color-key
masking, soft-mask precedence and grayscale transform handling.

The `.rgba` files contain expected straight image pixels from the JPEG corpus
or Pillow's independent native YCbCr output with the stated PDF transformations.
The `.page.rgba` files contain independent Poppler/Cairo page output at 72 DPI,
with antialiasing disabled. `corpus.json` records dimensions, component count and
PDF SHA-256. RGB reconstruction can differ slightly between JPEG decoders;
opaque image alpha and explicit mask alpha are exact.

These are authored media references with no external images, fonts or other
third-party content. JPEG application segments in `presentation-ignored.pdf`
deliberately carry unrelated data: PDF DCT interpretation ignores them.

The interpretation follows ISO 32000-1, §7.4.8/Table 13 and §8.9, including
the APP14 rules referenced in Adobe Technical Note 5116. The corpus does not
establish CMYK/YCCK, ICC conversion or complete PDF rendering support.

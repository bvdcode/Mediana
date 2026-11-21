# ICC color conversion

`SrgbConverter.Apply` and `SrgbPreview.Fit` apply embedded ICC profiles from
`IccColorDescription`. JPEG assembles numbered APP2 fragments into one owned
profile; HEIF, TIFF and WebP carry this same color description. HEIF preserves
`prof`/`rICC` identity through image derivations and both frame APIs. Decoding keeps
source samples and profile bytes. Conversion publishes straight RGBA8 sRGB with
the source profile removed. RGB/gray alpha is unchanged; CMYK output is opaque.

Supported profiles are ICC v2/v4 input, display, output and color-space profiles.
RGB/gray profiles use an XYZ connection space and matrix/TRC transforms:

- RGB colorant matrix and three tone curves, including shared curve tags.
- Grayscale `kTRC`, with replicated gray RGBA samples.
- Identity, single gamma and sampled curves with linear interpolation.
- Parametric curve functions 0 through 4.

CMYK profiles use a Lab or XYZ connection space and four-channel AToB transforms:

- `lut8Type` (`mft1`): 8-bit CLUT and input/output tables, Lab PCS.
- `lut16Type` (`mft2`): 16-bit CLUT and input/output tables, Lab or XYZ PCS.
  Lab uses the legacy 65280 encoding scale in both profile versions.
- `lutAToBType` (`mAB`): A curves, 8/16-bit CLUT and B curves, with optional
  paired M curves and affine matrix. Different grid sizes per input channel
  and shared complete curve elements are supported.

`A2B1` supplies the relative colorimetric transform. If absent, `A2B0` is selected
according to ICC tag-selection rules. A malformed or unsupported selected tag
fails; another intent is not tried. Four-dimensional CLUT interpolation uses
tetrahedra in three channels and linear interpolation in the fourth. Legacy
XYZ LUTs with eight-bit samples have no defined ICC PCS encoding and are rejected.

Conversion uses relative colorimetric intent, PCS D50 to D65 Bradford adaptation,
sRGB transfer and gamut clipping. Source matrix colorants are already in PCS D50;
`wtpt` or `chad` does not cause a second source adaptation. Black-point
compensation, RGB/gray LUTs, multi-process `mpet` elements, DeviceLink, abstract,
named-color and iccMAX profiles remain open. Unsupported models and malformed
profiles fail explicitly.

## API and policies

```csharp
using VideoFrame srgb = SrgbConverter.Apply(source, allocator, cancellationToken);
using VideoFrame preview = SrgbPreview.Fit(source, VideoDisplayMatrix.Identity,
    640, 480, allocator, cancellationToken);
```

`ApplyWithColorPolicy` and `FitWithColorPolicy` apply embedded ICC first and invoke
the supplied color-label resolver only for other descriptions. Embedded image
rendering and CLI image/animation conversion use these methods. Existing overloads
that accept an explicit `VideoColorInfo` retain their explicit override semantics.

An ICC transform cannot be represented by CICP labels alone, so
`VideoFormat.GetColorInterpretation` still rejects an ICC description. Passing raw
ICC-tagged samples directly to an sRGB encoder is also rejected; use a conversion
operation first.

JPEG with four components and an embedded profile returns `VideoPixelFormat.Cmyk32`:
four packed C/M/Y/K bytes, zero for no ink and 255 for full coverage, without
alpha. YCCK and Adobe inversion are reconstructed before applying the profile.
`SrgbConverter.Apply` produces RGBA32; `SrgbPreview.Fit` converts only the selected
CMYK pixels. `ImageResources.DecodeAsync` and CLI previews use this same pipeline.

Input curves are sampled once for all 256 channel values, then combined with the
colorant and destination matrices into vector contribution tables. A shared
16-bit linear-to-sRGB table replaces per-pixel power functions. Transform reuse
is keyed by immutable profile-description identity with weak lifetime tracking;
profiles are not retained by a permanent global content cache. Equal shared curve
tag extents reuse one sampled curve. Conversion creates one output frame; fitted
previews convert only the selected source pixels. Profile bytes, small lookup
tables and object overhead are outside the media-buffer allocator. CMYK keeps
input-curve cell coordinates for all 256 possible byte values and decoded CLUT
vectors; it does not allocate a full-size intermediate RGB frame. A transform
contains 12 bytes per CLUT node plus four 256-entry coordinate tables; profile
curves borrow the immutable profile data.

For a 2048×1024 CMYK frame and a reused FOGRA39 transform, repeated conversion
allocates approximately 8 MiB plus 2 KiB per call: the RGBA destination and small
frame/loop objects. The source occupies another 8 MiB. A 13×13 fitted preview
passes with an output media-buffer budget of exactly 676 bytes, independent of
the source dimensions. Transform tables and runtime overhead are additional.

## Verification

Ten procedural profiles cover RGB/gray, v2/v4, all supported curve types, distinct
channel curves, wide-gamut colorants and shared tag extents. LittleCMS references
use relative colorimetric intent with no black-point compensation. RGB permits
two 8-bit levels of rounding difference; alpha must match exactly. Managed and
Native AOT checks compare the output and verify fused preview sampling and resource
release. Managed checks also cover cancellation, concurrent use and explicit
rejection of malformed or unsupported profiles. CLI and embedded-image checks use the same profile pipeline.

Twelve additional procedural CMYK profiles cover `mft1`, `mft2`, `mAB`, Lab/XYZ,
v2/v4, unequal grid dimensions, all curve functions and shared curve elements.
On 4096 identical CMYK inputs per profile, the maximum RGB difference from
LittleCMS 2.19 is one level. The same comparison passes for 13 real FOGRA, SWOP,
GRACoL, IFRA and SNAP profiles. Managed and Native AOT runs verify both sets and
eight CMYK/YCCK JPEG conversions. JPEG reconstruction is checked separately
against libjpeg-turbo so IDCT rounding is distinct from ICC-transform error.

Managed checks cover malformed table extents, offsets, grids, curve counts,
padding and overlapping elements; intent selection; cancellation; concurrent
conversion; exact output-buffer budgets; Exif rotation; and embedded-image use
without invoking a missing-color policy. A 100×100 YCCK JPEG with a fragmented
FOGRA39 profile completed CLI PNG conversion; its complete output differed from
Pillow/libjpeg/LittleCMS by at most two RGB levels, with mean error 0.0117.

Six RGB profiles extracted from real presentations matched LittleCMS: three
matched every byte; the other three differed by at most one RGB level. Alpha
matched exactly. The references compare the same decoded image samples before
presentation composition. Conflicting JPEG chroma-position declarations use the
separate, explicit policy described in [JPEG decoding](jpeg.md).

A 16320×12240 HEIC photograph with a DCI-P3 D65/sRGB-transfer ICC profile
completed CLI PNG conversion. An independent PNG decoder verified and decoded
the complete output. At 16384 distributed pixels, the converted RGB differed
from LittleCMS by at most one 8-bit level; alpha matched exactly.

The profile, curve and LUT models follow [ICC.1:2022, clauses 6.3, 8.10, 10.5, 10.10–10.12, 10.18 and Annex F](https://www.color.org/specification/ICC.1-2022-05.pdf).
The destination transfer and adaptation follow the [ICC interpretation of sRGB](https://registry.color.org/rgb-registry/files/sRGB.pdf).

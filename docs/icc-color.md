# ICC color conversion

`SrgbConverter.Apply` and `SrgbPreview.Fit` apply embedded ICC profiles from
`IccColorDescription`. JPEG assembles numbered APP2 fragments into one owned
profile; HEIF, TIFF and WebP carry this same color description. HEIF preserves
`prof`/`rICC` identity through image derivations and both frame APIs. Decoding keeps
source samples and profile bytes. Conversion publishes straight RGBA8 sRGB with
the source profile removed and alpha unchanged.

Supported profiles are ICC v2/v4 input, display, output and color-space profiles
with an XYZ connection space and matrix/TRC RGB or gray transforms:

- RGB colorant matrix and three tone curves, including shared curve tags.
- Grayscale `kTRC`, with replicated gray RGBA samples.
- Identity, single gamma and sampled curves with linear interpolation.
- Parametric curve functions 0 through 4.

Conversion uses relative colorimetric intent, PCS D50 to D65 Bradford adaptation,
sRGB transfer and gamut clipping. Source matrix colorants are already in PCS D50;
`wtpt` or `chad` does not cause a second source adaptation. Black-point
compensation, LUT/multi-process transforms, Lab PCS, CMYK, DeviceLink, abstract,
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

Input curves are sampled once for all 256 channel values, then combined with the
colorant and destination matrices into vector contribution tables. A shared
16-bit linear-to-sRGB table replaces per-pixel power functions. Transform reuse
is keyed by immutable profile-description identity with weak lifetime tracking;
profiles are not retained by a permanent global content cache. Equal shared curve
tag extents reuse one sampled curve. Conversion creates one output frame; fitted
previews convert only the selected source pixels. Profile bytes, small lookup
tables and object overhead are outside the media-buffer allocator.

## Verification

Ten procedural profiles cover RGB/gray, v2/v4, all supported curve types, distinct
channel curves, wide-gamut colorants and shared tag extents. LittleCMS references
use relative colorimetric intent with no black-point compensation. RGB permits
two 8-bit levels of rounding difference; alpha must match exactly. Managed and
Native AOT checks compare the output and verify fused preview sampling and resource
release. Managed checks also cover cancellation, concurrent use and explicit
rejection of malformed or unsupported profiles. CLI and embedded-image checks use the same profile pipeline.

Six RGB profiles extracted from real presentations matched LittleCMS: three
matched every byte; the other three differed by at most one RGB level. Alpha
matched exactly. The references compare the same decoded image samples before
presentation composition. Conflicting JPEG chroma-position declarations use the
separate, explicit policy described in [JPEG decoding](jpeg.md).

A 16320×12240 HEIC photograph with a DCI-P3 D65/sRGB-transfer ICC profile
completed CLI PNG conversion. An independent PNG decoder verified and decoded
the complete output. At 16384 distributed pixels, the converted RGB differed
from LittleCMS by at most one 8-bit level; alpha matched exactly.

The matrix/TRC and curve model follows [ICC.1:2022, clauses 10.5, 10.18 and Annex F](https://www.color.org/specification/ICC.1-2022-05.pdf).
The destination transfer and adaptation follow the [ICC interpretation of sRGB](https://registry.color.org/rgb-registry/files/sRGB.pdf).

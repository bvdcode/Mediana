# SVG rasterization

`Mediana.Drawing` provides managed RGBA8 surfaces, vector paths, fill/stroke,
clipping, affine image drawing and alpha compositing. `Mediana.Images.Svg` reads
SVG and renders it on that surface. Both are independent Mediana source modules;
their initial algorithms derive from the same author's MIT-licensed Render.NET
code at commit `03cb502`. They do not load a browser, native graphics library,
3D engine or external runtime package.

```csharp
using Mediana.Drawing;
using Mediana.Images.Svg;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Codecs.Png;

SvgDocument document = await SvgReader.ReadFileAsync("image.svg", cancellationToken: cancellationToken);
PixelBuffer pixels = SvgRenderer.Render(document,
    new SvgRenderOptions { Width = 512, MaximumSamples = 4_000_000 }, cancellationToken);
MediaBufferAllocator allocator = new(16 * 1024 * 1024);
using VideoFrame frame = pixels.ToVideoFrame(allocator, cancellationToken);
using MediaBuffer png = PngEncoder.Encode(frame, allocator, cancellationToken);
```

Output is straight sRGB RGBA8. Features include paths and shapes, gradients,
transforms, local references, CSS declarations, fill/stroke rules, clipping,
opacity, masks, common filter primitives and TrueType text. Bundled Latin and
Cyrillic fonts are CC0; provenance and input hashes are recorded in the Fonts
folder. A caller can resolve fonts or images explicitly. The reader does not
fetch network resources or resolve external XML entities.

Embedded PNG decoding covers standard color/depth combinations and Adam7.
It is currently an internal SVG operation; it does not provide standalone PNG
color-profile handling. Unsupported SVG features are reported by
`UnsupportedFeatures`; strict rendering rejects them. SVG animation, patterns,
markers, complex CSS selectors, CFF/WOFF2 and complex-script shaping are outside
the implemented subset.

Input size, elements, nesting, path commands, embedded-image pixels,
supersampled output and effect workspace have separate bounds. The
`MediaBufferAllocator` above counts the copied frame and encoded PNG, not the
retained drawing surface or rendering workspace. These limits are not a whole
process memory cap.

Tests match 352 stored filter pixel references and exercise bundled fonts,
owned frame conversion, strict unsupported-feature handling, limits,
cancellation and external-entity rejection. Pixel references are regression
coverage, not complete SVG or browser conformance.

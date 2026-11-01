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
MediaBufferAllocator allocator = new(16 * 1024 * 1024);
using PixelBuffer pixels = SvgRenderer.Render(document,
    new DrawingRenderOptions { Width = 512, MaximumSamples = 4_000_000 }, cancellationToken, allocator);
using VideoFrame frame = pixels.PublishVideoFrame(cancellationToken);
await using FileStream output = new("preview.png", FileMode.CreateNew, FileAccess.Write, FileShare.None);
await PngEncoder.EncodeAsync(frame, output, allocator, cancellationToken);
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
`MediaBufferAllocator` above counts the final drawing surface and the PNG stream's
64 KiB plus 12 bytes of working storage.
Publishing transfers that surface to the frame without copying its RGBA bytes.
Rendering workspace has separate limits; these are not a whole process memory
cap. Idle effect surfaces are cleared before reuse and disposed when evicted
or when rendering finishes. Failed surface creation releases its budget.

Tests match 352 stored filter pixel references and exercise bundled fonts,
owned frame conversion, strict unsupported-feature handling, limits,
cancellation and external-entity rejection. Pixel references are regression
coverage, not complete SVG or browser conformance.

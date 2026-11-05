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

using SvgDocument document = await SvgReader.ReadFileAsync("image.svg", cancellationToken: cancellationToken);
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
folder. A caller can supply shared `TextOptions` or resolve images explicitly. The reader does not
fetch network resources or resolve external XML entities.

[TrueType glyph reading](fonts.md) describes outline bounds, ownership and
regression coverage shared with PDF previews.

Embedded images use the shared `Mediana.Imaging` preparation path. Direct reader
calls include managed PNG/JPEG decoders; `SvgReaderOptions.Images` supplies a
replacement context. When decoding through `MediaEngine`, the engine's registered
decoders are passed to embedded resources automatically, including asynchronous
implementations. Data URI content is recognized by its bytes. External files and
URLs still require the explicit caller resolver.

Image colors are converted to sRGB before drawing. Already canonical sRGB pixels
retain their storage; transformations allocate a separate converted frame.
Missing labels require an explicit `ImageColorResolver`, such as
`SdrPreviewColorPolicy.Resolve`; CLI previews select this policy and report
substitutions. PNG decoding covers standard color/depth combinations and Adam7.
Unsupported SVG features are reported by
`UnsupportedFeatures`; strict rendering rejects them. SVG animation, patterns,
markers, complex CSS selectors, WOFF2 and complex-script shaping are outside
the implemented subset.

Dispose `SvgDocument` after all renders finish to release retained raster images.
`SvgReaderOptions.ImageAllocator` supplies their decode and retained-storage
budget; `MaximumImageBytes` bounds that scope. Registered SVG decoding charges
retained images and workspace to the operation allocator as well as the local
image limit. An explicitly configured image allocator must belong to that budget.
Failed reads release images already loaded.
Use `SvgReader.ReadAsync` or `ReadFileAsync` for asynchronous image implementations.
The synchronous reader supports `ISynchronousImageDecoder` implementations and
rejects asynchronous-only decoders before starting them. Embedded resources
inherit the same decoder context and bounded nesting allowance.

Input size, elements, nesting, path commands, embedded-image pixels,
supersampled output and effect workspace have separate bounds. The
`MediaBufferAllocator` above counts the output, supersampling and temporary effect
surfaces, plus the PNG stream's 64 KiB and 12 bytes of working storage.
Publishing transfers that surface to the frame without copying its RGBA bytes.
`MaximumEffectBytes` is an additional effect workspace bound; these limits are
not a whole process memory cap. Idle effect surfaces are cleared before reuse and disposed when evicted
or when rendering finishes. Failed surface creation releases its budget.

Tests match 352 stored filter pixel references and exercise bundled fonts,
owned frame conversion, strict unsupported-feature handling, limits,
cancellation and external-entity rejection. Pixel references are regression
coverage, not complete SVG or browser conformance.

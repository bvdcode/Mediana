# TGS and Lottie animation

`Mediana.Images.Tgs` reads gzip-compressed TGS and Lottie JSON and rasterizes 2D
frames directly through `Mediana.Drawing`. It builds typed paths, paints, masks
and effects without serializing SVG or reparsing path strings. It is an independent
module, derived from the same author's MIT-licensed Render.NET algorithms at
commit `03cb502`. It has no JavaScript engine or external runtime dependencies.

```csharp
using Mediana.Images.Tgs;
using Mediana.Drawing;

TgsDocument document = await TgsReader.ReadFileAsync("animation.tgs", cancellationToken: cancellationToken);
foreach (TgsFrame frame in TgsRenderer.RenderFrames(document,
    new TgsAnimationOptions { FrameRate = 30, MaximumFrames = 300 },
    new DrawingRenderOptions { Width = 256 }, cancellationToken))
{
    // Consume frame.Image, frame.Timestamp and frame.Duration.
}
```

The frame sequence is lazy and ordered, with an explicit output-frame limit and
partial final-frame duration. Single-frame APIs accept an absolute source frame
or elapsed time. Features include 2D shapes, keyframes, transforms, gradients,
precompositions, repeaters, merge/round/offset paths, trim paths, masks, mattes,
blend modes and supported effects.

Programmatic expressions are explicitly rejected. They are not silently replaced
with saved property values. Text/image/external-asset layers, 3D scenes, property
slots and unsupported effects remain outside the implemented subset. Reader and
renderer limits bound compressed/decompressed input, scene items, nesting,
paths, samples and effect buffers; these are not a hard process-memory limit.

Tests compare every channel for 33 frames from eleven supported animations:
RGB allows one 8-bit code of rounding difference; alpha must match exactly.
Compressed pixel references are verified against the original stored hashes.
On Linux x64, ordinary .NET matches all original hashes; Native AOT differs in
four RGB channels across two pixels, each by one code. Four expression-bearing
compositions are rejection cases. Three
original synthetic animations also produce equal output from JSON and gzip at
four positions each. Timing, frame limits, cancellation and malformed input
bounds are checked separately. The hashes are renderer regression references,
not independent Lottie conformance evidence.

Concurrent renders of one composition use independent scene state and effect-buffer budgets.
A failed render releases temporary effect buffers; later renders can reuse the composition.

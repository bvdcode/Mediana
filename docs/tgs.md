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

The default reader rejects programmatic expressions and the default renderer
rejects unsupported visual effects. `TgsReaderOptions.IgnoreExpressions` explicitly
uses stored property values and keyframes without executing expression code;
properties without stored values remain errors. `IgnorePuppetEffects` explicitly
omits named `ADBE FreePin3` Puppet deformation. Both options default to false and
report their omissions through `TgsDocument.Warnings` and decoded-frame warnings.
The CLI enables both options for ordinary image conversion and `image tgs`,
logging one warning for each kind of omission present in the input.
Invalid keyframe times inside omitted Puppet data are not interpreted or repaired;
all input size, nesting, item-count and finite-number checks still apply.
Text/image/external-asset layers, 3D scenes, property
slots and unsupported effects remain outside the implemented subset. Reader and
renderer limits bound compressed/decompressed input, scene items, nesting,
paths, samples and effect buffers; these are not a hard process-memory limit.

Tests compare every channel for 33 frames from eleven supported animations:
RGB allows one 8-bit code of rounding difference; alpha must match exactly.
Compressed pixel references are verified against the original stored hashes.
On Linux x64, ordinary .NET matches all original hashes; Native AOT differs in
four RGB channels across two pixels, each by one code. Four expression-bearing
compositions are rejection cases for the default reader. Three
original synthetic animations also produce equal output from JSON and gzip at
four positions each. Timing, frame limits, cancellation and malformed input
bounds are checked separately. The hashes are renderer regression references,
not independent Lottie conformance evidence.

Concurrent renders of one composition use independent scene state and effect-buffer budgets.
A failed render releases temporary effect buffers; later renders can reuse the composition.

Unordered finite keyframe times are stably sorted before rendering. Tied times
retain their source order and property values remain unchanged. Missing or
non-finite times remain errors. Reordering is exposed in `TgsDocument.Warnings`
and through `ImageDecodeRequest.WarningHandler`; the CLI logs it as a warning.
Already ordered documents do not require JSON reconstruction.

Layer references use declared indices, including parent and Set Matte targets.
Unreferenced duplicate indices retain paint order; an ambiguous referenced index
is rejected. Parameter groups do not themselves create visual effects. RGB
Invert supports animated Blend With Original and preserves alpha; other Invert
channel selections remain unsupported.

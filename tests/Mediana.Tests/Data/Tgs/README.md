# TGS and Lottie references

The original orbit, effects and geometry animations are MIT-licensed synthetic
scenes authored for Render.NET, copyright 2026 Vadim Belov.

The fifteen feature-named Telegram TGS inputs originate from Render.NET commit
`03cb502` and were provided with permission for use as test cases. `manifest.json`
records their encoded hashes; `render-reference.json` contains 45 stored RGBA
hashes at three positions per animation. These are renderer regression references,
not independent Lottie conformance evidence.

Mediana rejects expression-bearing compositions. Tests compare reference pixels
for supported compositions and check explicit rejection for expression inputs.
The `.rgba.gz` files contain the 33 supported reference images and reproduce
the manifest hashes. The three `layer-controls.tgs` frames use the .NET 11
rotation behavior, which preserves a 0.001-degree rotation that .NET 10 rounded
to zero. A separate geometric sampling test verifies this rotation.
Every channel is compared, allowing one 8-bit
code for RGB rounding and requiring exact alpha. Linux x64 Native AOT differs
from ordinary .NET in four RGB channels across two pixels, each by one code.

# JPEG encoding

`Mediana.Codecs.Jpeg` provides `JpegImageEncoder : IImageEncoder` and
`JpegEncoder.EncodeAsync`. The encoder writes an 8-bit baseline sequential
Huffman JPEG directly to a writable stream. Seeking is unnecessary; the stream
remains open. Input is retained until completion and may be disposed by its
owner after encoding starts. A failed or cancelled destination must be discarded.

```csharp
JpegEncodingOptions options = new()
{
    Quality = 90,
    ChromaSampling = VideoChromaSampling.Sampling444
};
long bytesWritten = await JpegEncoder.EncodeAsync(
    rgbaFrame, destination, allocator, options, cancellationToken);
```

Input must be straight RGBA32 in known full-range sRGB with a known sample aspect
ratio. Quality is 0–100 (default 75); 100 still uses lossy DCT coding. Available
sampling modes are `Sampling420` (default), `Sampling422`, `Sampling444` and
`Monochrome`. Chroma reduction averages adjacent samples at centered JFIF sample
positions; incomplete edge blocks extend the edge samples. Quality scales the
example quantization tables from T.81 Annex K. Standard Annex K Huffman tables
are used without an image-wide statistics pass.

JPEG cannot retain alpha. The library rejects nonopaque input unless
`BackgroundColor` is supplied. That explicit setting composites straight alpha
against the selected RGB color in encoded sRGB. Callers requiring linear-light
compositing can composite first and pass opaque input. The CLI uses white.

Square samples carry an Exif sRGB declaration. Nonsquare samples use JFIF density
and an embedded ICC v2 sRGB or grayscale profile: Exif's square-pixel convention
would contradict their geometry. `Mediana.Formats.Color.IccProfiles` also exposes
these profiles for other format writers. JFIF density preserves the exact aspect
ratio. JPEG's 16-bit dimension and density fields limit each dimension and each
reduced aspect-ratio term to 65535. Progressive, arithmetic, lossless and CMYK
encoding are separate capabilities and are not produced by this encoder.

## CLI

```text
mediana -i input.png output.jpg --quality 90 --chroma-sampling Sampling444
mediana -i input.heic output.jpeg
mediana -i input.png gray.jpg --chroma-sampling Monochrome
```

JPEG automatically selects lossy encoding. `--image-compression Lossless` is
rejected. Input recognition and color conversion use the common image pipeline.

## Memory and implementation

Each call reserves 17,024 allocator bytes: a 16 KiB output buffer and 640 bytes
of quantization tables. Block transforms and conversion use bounded stack
storage, independent of image dimensions. There is no full-image intermediate,
coefficient plane, encoded-output buffer or background-composited copy. Runtime
object overhead, immutable shared tables and caller-owned stream storage are
outside the allocator budget. A memory pool can be supplied to the allocator.

Color conversion handles eight pixels together. The separable forward DCT uses
sum/difference decomposition and `Vector256<float>` across eight columns;
quantization also operates on vectors. Entropy coding packs canonical Huffman
codes and coefficient amplitudes, with byte stuffing and one-bit padding.
Cancellation is checked between MCUs and during asynchronous output writes.
Independent calls share immutable tables only.

## Validation and performance

Tests cover all sampling modes and quality endpoints, odd dimensions, color and
sample aspect ratio, alpha policy, independent asynchronous input ownership,
nonseekable output, cancellation, write failures, budget cleanup and concurrent
calls. The forward transform is compared with the direct double-precision DCT
definition. Native AOT executes the encoder and compares the complete decoded
image with an independent libjpeg reference. Existing baseline/progressive JPEG
decoding and CLI image tests pass after sharing the zigzag table.

A compatibility run encoded 48 images: 1×1, 19×13, 257×129 and a 768×512 photo;
four sampling modes; quality 1, 75 and 100. Pillow 12.3/libjpeg and FFmpeg decoded
every result. LittleCMS accepted the generated RGB and grayscale profiles and
preserved the decoded samples within one code value.

Measurements below use .NET 11, one fixed performance core of an Intel i5-12450H,
two available .NET logical processors, full optimized JIT, warm sources and 75
writes to a discard stream. The reference is ImageSharp 4.1.3 through
EasyExtensions.Drawing 3.0.95, with matching quality 75 and chroma sampling.
The chart includes text, fine lines and transparent edges composited onto white
before either encoder. Milliseconds are per encoding; allocation is total managed
allocation per call, including pooled-library object overhead. Shared-host
measurements are evidence for these inputs, not a universal performance claim.

| Input | Sampling | Initial implementation | Current | ImageSharp | Current CPU | Reference CPU |
|---|---|---:|---:|---:|---:|---:|
| Photo 768×512 | 4:2:0 | 7.832 ms | 2.796 ms | 1.854 ms | 2.818 ms | 1.871 ms |
| Photo 768×512 | 4:4:4 | 11.839 ms | 5.488 ms | 3.567 ms | 5.508 ms | 3.584 ms |
| Chart 1024×768 | 4:2:0 | 14.746 ms | 5.505 ms | 4.122 ms | 5.528 ms | 4.139 ms |
| Chart 1024×768 | 4:4:4 | 23.931 ms | 10.606 ms | 5.156 ms | 10.627 ms | 5.173 ms |

Managed allocation fell from approximately 66,142 to 17,502 bytes per call.
ImageSharp allocated approximately 17,268 bytes at 4:2:0 and 12,756–12,759 bytes
at 4:4:4. Process peak working sets were 44.4–47.9 MiB for Mediana and
48.4–51.9 MiB for the reference, including runtime and loaded input.
The optimized encoder produced exactly the same complete JPEG bytes as the
initial implementation for these four comparisons. Independent decoded PSNR
also matched ImageSharp to three decimal places: photo 37.136/38.513 dB and
chart 24.705/35.382 dB for 4:2:0/4:4:4 respectively. Visual photo crops showed the
same JPEG detail and edge artifacts. Mediana remains 1.3–2.1 times slower on
these comparisons; performance parity is not established.

The format and transform definitions follow [ITU-T T.81](https://www.w3.org/Graphics/JPEG/itu-t81.pdf).
Profile construction follows [ICC profile specifications](https://www.color.org/specification/ICC.1-2022-05.pdf)
and the ICC [sRGB interpretation](https://registry.color.org/rgb-registry/files/sRGB.pdf).

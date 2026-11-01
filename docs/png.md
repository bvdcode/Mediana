# PNG output

`Mediana.Codecs.Png` encodes straight RGBA32 sRGB frames as non-interlaced,
8-bit PNG files. It writes sRGB and sample aspect ratio metadata and uses BCL
zlib compression. Input must declare the color interpretation and sample aspect
ratio. Padded input rows are supported; padding bytes are excluded from output.

## Stream output

```csharp
using Mediana.Codecs.Png;
using Mediana.Core.Memory;

MediaBufferAllocator workingStorage = new(64 * 1024 + 12);
await using FileStream output = new("preview.png", FileMode.CreateNew,
    FileAccess.Write, FileShare.None, bufferSize: 64 * 1024,
    options: FileOptions.Asynchronous);
long bytes = await PngEncoder.EncodeAsync(frame, output, workingStorage, cancellationToken);
```

The operation retains a lease on the input pixels until completion. It reserves
64 KiB plus 12 bytes through the supplied allocator, including PNG chunk framing.
BCL compression workspace, objects and destination buffering have their own
lifetimes and are outside that allocation budget. Neither a complete compressed
image array nor another full RGBA array is created by this operation.

The destination may be non-seekable. All destination writes are asynchronous;
the encoder neither flushes nor closes it. The caller serializes access. The
return value counts bytes written by this operation, independently of the
destination's position. Invalid input, an already cancelled token or insufficient
working storage is rejected before the first write. A later error or cancellation
can leave partial output; discard it. The CLI writes to a temporary file and
publishes the completed file through its existing output transaction.

One zlib datastream spans consecutive IDAT chunks, each carrying at most 64 KiB.
Chunk boundaries do not reset compression, force row boundaries or flush the
compressor. This follows [PNG third edition, section 10.2](https://www.w3.org/TR/png-3/#10CompressionFSL).

## Owned buffer output

`PngEncoder.Encode(frame, allocator, cancellationToken)` returns an independently
owned `MediaBuffer`. The caller disposes it. Its allocation budget counts the
complete reserved array, including unused compression capacity. At 2560×2560
RGBA pixels, that reservation is 29,903,822 bytes; slicing the returned buffer
does not reduce the retained allocation. Use this operation when an owned PNG
buffer is required, and the stream operation when writing to a file or response.

## Verification

Shared managed and Linux Native AOT checks cover exact compressed payloads and
visible pixels, padded rows, non-square sample aspect ratio, wide scanlines,
multiple IDAT chunks, every chunk CRC, asynchronous-only non-seekable output and
source ownership during a deliberately pending write. They also cover early
budget rejection, cancellation and write failures at the header, intermediate
data, final data and IEND, preserving the original error without further writes
or retained working storage.

A serial allocation regression test warms up the encoder and then measures three
complete 1000×500 encodes to a discard stream. The permitted allocation is less
than one quarter of the RGBA image size per operation, rejecting a full pixel
clone or a complete encoded-capacity array even if it bypasses the media budget.

Three alternating pairs of fresh Linux x64 Native AOT CLI processes, pinned to
one CPU core, produced these medians for complete synthetic conversions:

| Input | CPU before / stream | Peak RSS before / stream |
| --- | ---: | ---: |
| PDF RGB image, 2560×2560, incompressible samples | 1.0904 / 1.1178 s | 157408 / 136640 KiB |
| PDF RGB image, 2560×2560, constant samples | 0.4788 / 0.4642 s | 101888 / 101928 KiB |
| SVG solid rectangle, 1280×1280 | 0.3708 / 0.3690 s | 51840 / 51584 KiB |

All 18 complete outputs passed every chunk CRC and every source RGBA component.
Six independent full PNG decodes, one per input and implementation, also matched
the source pixels exactly. IDAT framing changes file bytes when the compressed
payload spans multiple chunks; it does not change the pixels.

A separate Native AOT allocation measurement used a 2560×2560 constant RGBA
frame, two warmups and five complete encodes to a discard stream per process.
Across three fresh processes per operation, allocation was 29,904,256 bytes per
owned-buffer encode and 66,272 bytes per stream encode, excluding input setup.
The media working reservation was 65,548 bytes. Allocation, media reservations
and process peak RSS describe different costs. These shared-host measurements
establish the observed memory change; they do not establish a general speedup.

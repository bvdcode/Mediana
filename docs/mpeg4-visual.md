# MPEG-4 Visual video

`Mediana.Codecs.Mpeg4Visual` reconstructs MPEG-4 Part 2 video into native 8-bit
YUV 4:2:0 frames. It does not depend on a native codec or external executable.
Register `Mpeg4DecoderFactory` with `MediaEngineBuilder.AddVideoDecoder` and the
required container reader separately:

```csharp
MediaEngine engine = new MediaEngineBuilder()
    .AddContainer(new AviReaderFactory())
    .AddContainer(new Mp4ReaderFactory(new()
        { TimestampMode = Mp4TimestampMode.LinearMovie }))
    .AddVideoDecoder(new Mpeg4DecoderFactory())
    .Build();

await using VideoSession session = await engine.OpenVideoAsync(
    source, allocator, new MediaOpenOptions(), streamIndex: null,
    cancellationToken: cancellationToken);

while (await session.ReadFrameAsync(cancellationToken) is VideoFrame frame)
{
    using (frame)
    {
        Console.WriteLine($"{frame.Timestamp}: {frame.Format.Width}x{frame.Format.Height}");
    }
}
```

The required namespaces are `Mediana`, `Mediana.Codecs.Mpeg4Visual`,
`Mediana.Containers.Avi`, `Mediana.Containers.Mp4` and `Mediana.Core.Video`.
Sources, output destinations and allocation policy belong to the caller.
The same decoder participates in preview generation, indexed seeking and
`MediaEngine.ProcessAsync`; there is no separate command-line decoding path.

## Reconstruction and framing

Implemented tools include progressive I/P/B pictures, spatial DC/AC prediction,
H.263 and MPEG quantization, custom matrices, all three coefficient escape modes,
one/four-vector motion, half/quarter-pixel prediction, bidirectional/direct
prediction, resynchronization packets and odd picture dimensions. Output metadata
preserves declared sample aspect ratio and available visual-object color signals.
Interlaced pictures, global-motion sprites, data partitioning, new prediction,
nonrectangular shapes and extended quantizer precision are rejected explicitly.
Encoding MPEG-4 Part 2 is separate work.

`StreamFormats.Mpeg4Visual` describes one complete VOP per access unit, with
optional preceding start-code headers. Configuration may be supplied out of band
or in the opening packet. MP4 maps the `mp4v` sample entry and validates the
`esds` visual object type and decoder configuration. AVI maps declared MPEG-4
FourCCs to this same stream representation; unrelated FourCCs retain their
original stored representation.

The public codec follows the shared send/receive protocol, including output
backpressure, end-of-input draining and reset. A failed or cancelled codec releases
its buffers and requires reset before further use. Returned frames retain their
own leases and remain valid after decoder disposal. Two completed reference
pictures are retained; reconstruction writes directly into output storage and
never adds a second full-frame pixel copy.

## Presentation and random access

The VOP clock uses the layer's exact time-increment resolution and group-of-VOP
time bases. B pictures use the surrounding reference interval. Presentation
reordering uses 64-bit positions, including clocks beyond `Int32`. Known packet
PTS and durations retain the caller's timeline. Missing PTS can be obtained from
the encoded VOP clock; frame duration is not guessed from neighboring coded
packets by the codec.

A fixed picture period is distinct from a VOP's fractional within-second clock.
A period equal to the resolution correctly represents one picture per second.
AVI retains its chunk decode clock separately and reconstructs relative VOP
presentation times and display extents. Ordinary AVI indexing reads picture-header
prefixes without reconstructing pictures. Declared packed DivX/Xvid transport
structurally scans reference chunks, splits multiple VOPs and removes validated
transport placeholders; clients receive the same complete access-unit contract. MP4 retains its indexed composition offsets and explicit
movie edits.

`Mpeg4RandomAccessInspector` validates a coded intra VOP against the applicable
layer configuration. Indexed decoding restarts from that point and consumes the
required following coded packets. Leading open-GOP B pictures that require the
unavailable previous GOP are discarded before the requested presentation point.
The complete stream still decodes those pictures when both references exist.

## Independent verification

Complete sequences from FFmpeg's native MPEG-4 encoder and libxvid cover I/P/B
pictures, both quantization modes, quarter-pixel and four-vector motion,
rate-distortion/trellis encoding, noise, odd geometry and 30000/1001 cadence.
Tests compare every visible Y/U/V sample against independent reconstruction,
including reset, retained frame ownership and allocation failure.
MPEG-4 Visual permits IDCT numerical differences; validation records sample error
and PSNR instead of declaring all conforming decoders bit-identical.

Three independently muxed AVI/MP4 pairs decode to the same reference pictures as
their elementary streams. Shared public operations check full decoding, every
reverse seek position, SDR previews, complete AVC output and selected intervals.
MP4 output timing is checked against independent frame PTS and duration records.
A packed Xvid AVI verifies normalization without changing its 22 displayed
pictures. A separate one-fps sequence checks the fixed-period boundary.

## Complete-operation performance

[Paired decode measurements](performance/results/2026-11-07-mpeg4/README.md)
record all trials, CPU, elapsed time, RSS, managed allocations and independent
quality checks. SIMD prediction and sparse-coefficient processing reduce CPU
about 9–15% across the three sources without an additional full-frame buffer.
The noisy source remains substantially slower than FFmpeg.

Syntax research uses [ISO/IEC 14496-2](https://www.iso.org/standard/39259.html),
the [MPEG reference-software catalogue](https://mpeg.chiariglione.org/technologies/reference-software/mpeg-4-reference-software.html),
[ARIB STD-B24 Annex F](https://www.arib.or.jp/english/html/overview/doc/6-STD-B24v5_2-1p3-E1.pdf),
and independent [FFmpeg](https://github.com/FFmpeg/FFmpeg/blob/n7.1/libavcodec/mpeg4videodec.c)
and [JCodec](https://github.com/jcodec/jcodec/tree/master/src/main/java/org/jcodec/codecs/mpeg4)
implementations. These are reference materials, not runtime dependencies.

# DV25 video

`Mediana.Codecs.Dv` decodes 625/50 and 525/60 DV25 from complete DIF frames.
PAL output is 720×576 YUV 4:2:0 at 25 fps. NTSC output is 720×480 YUV 4:1:1
at 30000/1001 fps. Timestamps come from the AVI stream's clock. The decoder preserves
interleaved field rows and reads field order and aspect ratio from the video
source control pack. An explicit OpenDML `vprp` sample aspect ratio takes
precedence.

Register `DvDecoderFactory` from `Mediana.Codecs.Dv` and `AviReaderFactory`
from `Mediana.Containers.Avi` with `MediaEngineBuilder`. The AVI reader exposes
`dvsd` video tracks as `dvvideo`, with one independently decodable frame per
packet. Use `VideoSession.SeekAsync` and `ReadFrameAtOrAfterAsync` to select by
presentation time.

```text
mediana -i input.avi preview.png --time-ms 2275
```

The AVI mapping covers Type-2 video tracks. The shared CLI preview policy
converts interlaced input to progressive PNG.

The [PAL AVI fixture](../tests/Mediana.Containers.Avi.Tests/Data/Dv/README.md)
checks frame dimensions, timing and decoded Y/U/V samples at the start and after
a seek between timestamps. The PAL check also runs in the Native AOT consumer. The synthetic NTSC
fixture checks the same properties against an independent decoder.

See [Microsoft's DV in AVI specification](https://learn.microsoft.com/en-us/windows/win32/directshow/dv-data-in-the-avi-file-format)
for the AVI stream mapping and [RFC 6469](https://www.rfc-editor.org/rfc/rfc6469.html#section-2.1)
for the IEC 61834 DIF layout.

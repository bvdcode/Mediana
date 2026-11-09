# AV1 container mapping

MP4 `av01` and Matroska `V_AV1` expose the same `av1.codec-configuration-record` configuration and `av1.low-overhead.access-unit` packets. Container recognition is independent of the decoder's supported reconstruction tools.

`Av1CodecConfigurationRecord` validates the four-byte record, profile/depth/chroma relationships, reserved fields and configuration OBU boundaries. It accepts Main, High and Professional configurations. The reconstruction module applies its own profile and tool coverage afterward. Sequence-header consistency is checked by the decoder.

The shared `Av1ObuReader` preserves temporal and spatial extension identifiers. Configuration OBUs require explicit sizes. Access units may omit the final OBU's size, in which case that OBU consumes the rest of the packet. The decoder still rejects unsupported scalability explicitly.

The binding follows [AV1 ISOBMFF 1.3.0](https://aomediacodec.github.io/av1-isobmff/), [Matroska codec mappings](https://www.matroska.org/technical/codec_specs.html#av1) and the [AV1 specification](https://aomediacodec.github.io/av1-spec/).

## Validation

The five-picture `interOrdered.obu` sequence is independently remuxed with FFmpeg 6.1.1 at 24 fps. Both containers decode through `MediaEngine` and preserve every YUV sample of the independent reference. Backward and repeated indexed frame selection checks presentation timestamps, restart after EOF and released buffer ownership. A separate public-factory sequence test removes the final OBU's size field in every packet and verifies the same samples and restart decisions.

| Fixture | SHA-256 |
| --- | --- |
| `interOrdered.mp4` | `fbd1fce2b16f3a26e1a64b57b37dc4e2b2493799677539ed00b4d0644da645a6` |
| `interOrdered.mkv` | `080662b7aaacc8995cf0b7de9151ed879626608555a3f7f4145d58657fc4b305` |

These container checks do not establish support for every AV1 reconstruction tool. The decoder's coverage is documented separately.

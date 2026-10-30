The encoded references exercise the managed H.264 encoder using the existing
MP4 source pictures. `ENCODING.json` records the input hashes, fixed quantizers,
picture counts, complete reference YUV hashes and measured sample errors.

The encoder performs intra4x4 DC prediction, integer transforms, scalar
quantization, chroma DC transforms and CAVLC coding. Each picture is a separate
IDR in Constrained Baseline level 4.1; deblocking is disabled. These are compressed
residuals, with no I_PCM macroblocks. Output preserves visible dimensions,
sample aspect ratio, color metadata and packet presentation times.

The following commands generate each reference. Set `NAME` and `QP` to the
manifest case, with a new output filename:

```sh
dotnet run --project tools/Mediana.Cli -c Release -- \
  -i "tests/Mediana.Tests/Data/Mp4/$NAME.mp4" "artifacts/$NAME-qp$QP.ts" \
  --video-codec H264 --video-only --qp "$QP"
ffmpeg -v error -xerror -threads 1 -i "artifacts/$NAME-qp$QP.ts" \
  -map 0:v:0 -fps_mode passthrough -pix_fmt yuv420p -f rawvideo \
  "tests/Mediana.Tests/Data/Mp4/$NAME.encoded-qp$QP.yuv"
```

Reference decoding used FFmpeg 6.1.1's H.264 decoder on every emitted picture.
Managed and Native AOT checks reconstruct the encoded packets and compare every
visible sample to these independently decoded references. They also verify IDR
restart headers, timing, color, SAR, completion and allocation release. This is
an interoperability and reconstruction check; the small corpus does not
establish general encoder quality, bitrate efficiency or throughput.

Normative source: ITU-T H.264 V15 (08/2024), clauses 7.3, 8.3, 8.5, 9.1, 9.2,
Annexes A and E. VLC codewords reuse the module's existing normative tables.
Forward transform and quantization are encoder decisions; decoding follows
the normative inverse processes. No FFmpeg encoder is used in this step.

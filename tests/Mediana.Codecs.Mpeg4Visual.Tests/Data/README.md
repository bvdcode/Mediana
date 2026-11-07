# MPEG-4 Visual reconstruction fixtures

The synthetic `testsrc2` sequences were encoded by FFmpeg's native `mpeg4`
encoder with one thread, quality 4, GOP 12 and 12 pictures/s for two seconds.
`simple` has no B pictures; `bframes` uses up to two. `quarter` also enables
quarter-pixel and four-vector motion. `matrix` enables MPEG quantization.
Geometry is 96×80, except `quarter` at 128×96.

The complete `.yuv` files contain independent FFmpeg reconstruction in display
order, with native 8-bit Y, U and V planes. No color conversion or scaling is
performed. MPEG-4 Visual permits conforming IDCT implementations to differ;
sequence comparisons report sample error and PSNR instead of assuming a
normatively fixed integer transform.

These files were created from the test generator and contain no user media.

Primary syntax and algorithm references:

- [ISO/IEC 14496-2](https://www.iso.org/standard/39259.html)
- [MPEG-4 reference software catalogue](https://mpeg.chiariglione.org/technologies/reference-software/mpeg-4-reference-software.html)
- [FFmpeg MPEG-4 Visual decoder](https://github.com/FFmpeg/FFmpeg/blob/n7.1/libavcodec/mpeg4videodec.c)
- [JCodec MPEG-4 decoder](https://github.com/jcodec/jcodec/tree/master/src/main/java/org/jcodec/codecs/mpeg4)

Additional complete sequences cover 256×192 motion at 24 fps with rate-distortion
macroblock decisions and trellis, temporally varying noise, 30000/1001 cadence,
and odd 97×81 geometry. The `xvid` sequence uses the independent libxvid encoder
at 128×96 with quarter-pixel motion and B pictures. It is demultiplexed from AVI
and normalized with `mpeg4_unpack_bframes` before raw-stream reference decoding;
22 coded pictures are retained. Raw-stream tests contain complete individual VOPs.

The `bframes`, `fractional` and `quarter` sequences also have independently muxed
AVI and MP4 variants. Their complete decoded YUV references equal the corresponding
raw-stream references byte for byte. MP4 `.frames.json` files contain FFprobe
presentation times, durations and the rational clock after movie edits. AVI lacks
an explicit PTS table: its reader reconstructs the VOP presentation clock relative
to the first picture and retains the separate AVI chunk decode clock. It reads
picture-header prefixes to build that index without decoding or retaining the
complete compressed movie. Every display position is tested in reverse order,
including open-GOP leading B pictures discarded at random access.

`fixed-one-fps` is a three-picture 16×16 sequence at one picture per second. Its
VOL declares a clock resolution and fixed picture period of one. The original
FFmpeg stream has variable-rate signalling; the fixture changes only the fixed
rate flag, inserts the one-bit period and realigns header stuffing. Independent
FFmpeg decoding produces the same complete YUV sequence before and after the
header change. The regression checks the entire output and exact 0, 1, 2 second
presentation clock. A fixed picture period is distinct from a VOP's fractional
within-second time increment; its ratio to the resolution defines cadence.
See [ARIB STD-B24, Volume 1, Annex F, Note 2](https://www.arib.or.jp/english/html/overview/doc/6-STD-B24v5_2-1p3-E1.pdf).

`partial-macroblock` contains 37 pictures at 96×72, with half-pixel four-vector
motion and B pictures. The bottom eight visible luma rows share a partially
visible macroblock. Its complete I420 reference was decoded by independent Xvid
1.3.7 using `xvid_decore`, one thread, and no postprocessing. The test compares
every visible Y, U and V sample in display order, requires at least 48 dB PSNR
and maximum error six per picture, and verifies all picture storage is released.

The source is the first 37 coded VOPs, including their original opening headers,
from this synthetic 96-picture sequence:

```sh
ffmpeg -f lavfi -i 'testsrc2=size=96x72:rate=24,noise=alls=35:allf=t+u:all_seed=6' \
  -frames:v 96 -c:v mpeg4 -q:v 8 -flags +mv4 -mbd rd -trellis 1 -bf 2 \
  -g 48 -threads 1 partial.avi
ffmpeg -i partial.avi -map 0:v:0 -c:v copy -f m4v partial.m4v
```

The retained prefix ends immediately before the 38th VOP start code. Xvid emits
exactly 37 pictures, including the final delayed reference picture. The `.m4v`
SHA-256 is `d4e02e066c62110ae882bde7cfcdad36fa9e9e2bd994134d1df1478d78fd4464`;
the 383,616-byte `.yuv` SHA-256 is
`315bec5f3eb89d4d56846208cb3b206e333984618a2e14f039b7130a7fd3cce8`.

[Xvid 1.3.7 source](https://downloads.xvid.com/downloads/xvidcore-1.3.7.tar.gz),
`src/image/image.c`, documents reference padding at macroblock boundaries under
ISO/IEC 14496-2 clause 7.6.4. This fixture distinguishes those reconstructed
padded samples from clipping each 8×8 prediction's origin to the visible image
boundary. Decoder-specific numerical differences are evaluated against the
stated reconstruction tolerance.

The `xvid.avi` fixture retains the original DivX packed transport declaration.
Its 24 AVI transport ticks contain 22 coded pictures, two initial empty chunks
and uncoded transport placeholders. Decoding the original AVI with FFmpeg
produces exactly the existing 22-picture `xvid.yuv` reference. The reader splits
multiple VOPs within a chunk, retains original chunk validation extents and
physical decode clocks, and exposes separate exact presentation durations.
Unlike ordinary AVI, opening packed AVI structurally scans reference chunks
to locate VOP boundaries with bounded scratch; it retains only index entries.

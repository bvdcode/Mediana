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

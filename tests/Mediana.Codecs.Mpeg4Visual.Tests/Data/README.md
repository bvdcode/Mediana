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
22 coded pictures are retained. Packed transport behavior is checked separately
when supported by the container API.

# ProRes reference pictures

Seven MOV files contain three procedural 82x74 frames at 12 fps, encoded with
FFmpeg 6.1.1 prores_ks. Proxy, LT, Standard, HQ, 4444 and 4444 XQ correspond to
profile indexes 0 through 5. The field case uses HQ, interlaced DCT and top-first
field order. The non-multiple-of-16 dimensions exercise cropped edge slices.

The source is testsrc2. The 4444 inputs add a changing spatial alpha pattern using
geq with `mod(X*17+Y*11,1024)`. Independent FFmpeg decoding produces little-endian
YUV422P10 references, or YUVA444P12 for 4444. ProRes color samples are compared
within two code values after accounting for the reference decoder's clipping to
4 through maximum-minus-4. Mediana uses the full range specified by SMPTE RDD 36.
Alpha matches exactly after the reference decoder's 16-to-12-bit truncation.

The legacy encoder pads bottom alpha slices to sixteen rows and can omit a final
single-sample run marker. Those files require explicit RecoverLegacyAlpha;
separate packet tests cover strict normative 8- and 16-bit lossless alpha.

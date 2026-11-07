Theora video fixtures contain synthetic moving test patterns encoded by libtheora through FFmpeg. No external media is included.

- `main420.ogv`: 96×80, YUV 4:2:0, 12 fps, 24 pictures, key interval 12, quality 5.
- `chroma422.ogv`: 65×49 display inside an 80×64 frame, YUV 4:2:2, 8 fps, 8 pictures, key interval 4, quality 8.
- `chroma444.ogv`: 96×64, YUV 4:4:4, 8 fps, 8 pictures, key interval 4, quality 6.

The first stream uses the `testsrc2` source; the others use `testsrc`. Generation selects `-c:v libtheora`, the stated pixel format, `-threads 1` and `-q:v`/`-g` settings. Headers and setup syntax follow the [Xiph Theora specification](https://www.theora.org/doc/Theora.pdf), version 3.2.1.

`idct.bin` stores 512 one-dimensional and 512 two-dimensional integer transform input/output vectors. Inputs cover basis impulses and deterministic full-range signed coefficients. Expected values come from the generic C transform in Xiph libtheora revision `28fd5ec77f0ad0e07a371cef1047828116f6bd8a`; only numerical data is included. The managed transform implements the normative arithmetic in section 7.9.3, including signed truncation.

Each `.framehash` contains FFmpeg SHA-256 hashes over all visible native YUV planes, in presentation order with `-fps_mode passthrough`. The opening `.yuv` files permit exact pixel diagnostics.

Additional sequences cover 48 moving 256×192 pictures (`motion`, quality 4), 24 noisy 128×96 pictures (`noise`, quality 1), an odd 65×49 4:2:0 crop (`odd420`, quality 8), and a constant-color source (`duplicate`). The constant-color export contains one coded picture; explicit zero-byte repeat packets are tested separately.

`fractional.ogv` uses 30000/1001 fps with a key interval of six. `av.ogv` multiplexes 12-fps Theora video with 48-kHz mono Vorbis from a 617-Hz synthetic sine wave. These exercise exact clocks and independent logical-stream indexes.

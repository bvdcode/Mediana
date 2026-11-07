Theora video fixtures contain synthetic moving test patterns encoded by libtheora through FFmpeg. No external media is included.

- `main420.ogv`: 96×80, YUV 4:2:0, 12 fps, 24 pictures, key interval 12, quality 5.
- `chroma422.ogv`: 65×49 display inside an 80×64 frame, YUV 4:2:2, 8 fps, 8 pictures, key interval 4, quality 8.
- `chroma444.ogv`: 96×64, YUV 4:4:4, 8 fps, 8 pictures, key interval 4, quality 6.

The first stream uses the `testsrc2` source; the others use `testsrc`. Generation selects `-c:v libtheora`, the stated pixel format, `-threads 1` and `-q:v`/`-g` settings. Headers and setup syntax follow the [Xiph Theora specification](https://www.theora.org/doc/Theora.pdf), version 3.2.1.

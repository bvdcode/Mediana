# VP9 bitstream corpus

These streams were independently encoded with libvpx-vp9. The adjacent JSON files record FFmpeg frame metadata. IVF is a packet fixture format, not a supported Mediana container.

| Stream | Profile | Precision | Sampling | Additional coverage |
| --- | --- | --- | --- | --- |
| main8 | 0 | 8 bits | 4:2:0 | Key and inter frames, odd height |
| main10 | 2 | 10 bits | 4:2:0 | High precision |
| high8 | 1 | 8 bits | 4:4:4 | Unsubsampled chroma |
| high12 | 3 | 12 bits | 4:4:4 | High precision |
| lossless | 0 | 8 bits | 4:2:0 | Lossless quantization |
| resilient | 0 | 8 bits | 4:2:0 | Error resilient coding |
| tiles | 0 | 8 bits | 4:2:0 | Two tile columns and two tile rows |
| altref | 0 | 8 bits | 4:2:0 | Two-pass encoding, hidden reference pictures, superframes |

The first seven streams use the FFmpeg test source at 8 frames/s for two seconds, normally 96 × 65 pixels; the tiled stream is 512 × 130. The alternate-reference stream uses testsrc2 at 128 × 80, 12 frames/s for four seconds, a 48-picture key-frame interval, a 25-picture lookahead and a 150 kbit/s target.

Header tests cover visible frame dimensions, profile, sample precision, sampling, reference geometry, coding flags, superframe bounds and truncated headers. They do not establish pixel decoder conformance.

`boolean.bin` was produced by the libvpx arithmetic writer for 4096 decisions. Decision `i` has probability `(i * 73 + (i >> 3) * 19) & 255` and value `((i * 1103515245 + 12345) >> 16) & 1`, using 64-bit arithmetic. The writer inserts the required zero marker and final padding. This vector covers probabilities 0 through 255 and is decoded without an encoder dependency.

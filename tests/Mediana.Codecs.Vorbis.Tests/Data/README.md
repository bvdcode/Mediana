Vorbis codebook references use Xiph libvorbis 1.3.7 and libogg 1.3.5.

`codebooks.bin.gz` contains 96 complete setup headers and 49,152 independently encoded scalar symbols with vector lookups. Cases cover ordered and unordered lengths, sparse entries, depths through 32, lookup types 0/1/2, dimensions 1 through 8 and sequenced quantization. Each record contains setup bytes, dimensions, entry count, lookup type, encoded symbols, expected entry indices and the expanded reference vectors. Integers and floats are little endian; arrays begin with a signed 32-bit length. The decompressed file begins with `VORBOOK1` and the case count.

SHA-256: `4ea4aceceddf44511b5a1e8d105e6a81d8553b59fd894c4837ffb43354548248`.

`spectrum.bin.gz` contains 1,183 packets from 32 streams encoded and decoded with Xiph libvorbis 1.3.7. Sample rates span 8–96 kHz, channel counts 1–8, four quality settings, transient/noise/tonal input, changing block sizes, coupling and submaps. `synthetic-spectrum.bin.gz` contains 192 additional cases covering both floor types, all three residue types, nonaligned partitions and truncated audio packets.

Both files decompress to `VORSPEC1`, a 32-bit stream count, then length-prefixed identification/setup headers and a packet count. Each packet stores length-prefixed bytes, 64-bit granule, 32-bit end flag, one length-prefixed planar float spectrum per channel, and a 32-bit sample count followed by planar float PCM. Numeric fields use little endian.

SHA-256 `spectrum.bin.gz`: `710cd089da9e4f6c047c6f6d844dc3d804e81262997c26946ed024854a9ec2c2`.

SHA-256 `synthetic-spectrum.bin.gz`: `3c6940857aaa0b1ce7ff4fff4ae1fb272d773acc8b86c032c5a43afc0c2a4686`.

`inverse-mdct.bin.gz` contains 128 independent Xiph inverse transforms: all block sizes 64–8192, impulses, tones, noise, cancellation patterns and amplitude scales. Decompressed `VORMDCT1` is followed by a 32-bit case count; each case stores a 32-bit block size, its half-size float spectrum and full-size float output. All fields use little endian.

SHA-256: `3bab0493ed8151d13c3298d62ae5819006d2b64145651d14503bd1720e3bd2eb`.

`ogg-stereo.f32le` contains the continuous interleaved stereo float PCM from `vorbis-stereo.ogg`, decoded with Xiph libvorbisfile 1.3.7: 60,417 samples per channel at 44,100 Hz. The integration tests verify whole-stream output, final granule trimming and repeated forward/reverse indexed seeks.

SHA-256: `ba5d753b32e371948254ab74621d781f00386a12510aa4f9afef6fa420df6447`.

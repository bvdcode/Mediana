The ADTS fixtures contain the original compressed access units from the
`lc-mono-48.m4a` and `lc-stereo-44.m4a` AAC fixtures, remuxed with FFmpeg 6.1.1:

```sh
ffmpeg -nostdin -v error -xerror -i input.m4a -map 0:a:0 -c:a copy -f adts output.aac
```

The tests compare every decoded sample with the existing independent float32
references in `Mediana.Tests/Data/Aac`. No container edit or gapless trimming is
applied; the references include every complete coded AAC frame. The mono input
uses 48 kHz, and the stereo input uses 44.1 kHz.

`silence-1.aac`, `silence-2.aac`, `tone-1.aac` and `tone-2.aac` contain six
protected AAC-LC ADTS frames each. FDK AAC 2.0.3 encoded 4096 generated 48 kHz
PCM16 samples at 32 kbit/s, using `AACENC_AOT=2`, `AACENC_TRANSMUX=2` and
`AACENC_PROTECTION=1`. The upstream codec and transport modules were unchanged.
The inputs are silence and sine tones with a peak PCM16 magnitude of 9000;
the stereo tone channels use different frequencies. The original FDK decoder accepts every frame and rejects a stream
with only a CRC-word bit changed.

The silence frames exercise short channel elements, zero padding to 192 and
128 protected bits, and overlapping CPE protection. The tone frames also cover
elements longer than their protected prefixes. All 24 transmitted CRC words
match the normative syntax regions in ISO/IEC 14496-3:2001, Annex 1.A.3.2.1.

| File | SHA-256 |
| --- | --- |
| silence-1.aac | 0449c2ab55c9f720ffc7bf5ef55d9239b5252d62f28789449e1cd57022d424ed |
| silence-2.aac | 3b725c28ac5bc35887236179fdcb8cf2974c926f4359553b6c12d4812a147d8d |
| tone-1.aac | fab081555ba94661dbf9d588d8ef49702b6baf04b5c84f1092c5a75b376cd091 |
| tone-2.aac | 5e95b3c135b1d7e66448e05ac569824f6024d944c31b36b33d0db512828d6dfc |

`data-element.aac` prepends a 32-byte aligned data-stream element to each
`silence-1.aac` raw block and recalculates the transport header and CRC. Its
data element has tag zero and byte values 0–31. The unchanged FDK decoder accepts
all six frames and rejects a bit change in the last data byte, beyond the first
192 DSE bits. This verifies whole-element coverage separately from the fixed
channel prefix lengths. Its SHA-256 is
`b2dd718fc7db611d561a85721ed1f6a3243b8e688ec259c70720ff6dd2df851d`.

`multi-protected.aac` and `multi-unprotected.aac` regroup the six original
`tone-2.aac` access units, repeated three times, without changing compressed
audio. Their 11 transport frames contain 1, 2, 3, 4, 2, 1, 1, 1, 1, 1, 1 raw
blocks. Frame lengths, block-position tables and CRCs follow
ISO/IEC 14496-3:2001/Cor 1:2002, Annex 1.A.3.2. VBR fullness is 0x7ff.
The original FDK AAC 2.0.3 decoder, with its PCM limiter disabled, reconstructs
all 18 access units in each file. All 73,728 PCM16 output bytes equal the original
single-block sequence; their SHA-256 is
`c1a01ccbab8e59277f1e233e7feeba7ba86c71d0339b460acf59a66b7826545e`.
The complete single-block ending also provides the lookahead required by that
decoder's ADTS multi-block transport availability checks.

| File | SHA-256 |
| --- | --- |
| multi-protected.aac | d53e3137fbcc23dee35763c7d637525a04900e48ca5f874b6a939961d0d5605a |
| multi-unprotected.aac | f00d55d53230814b158124432664a0a73723221c2e26d977eadf6ef3c4a709f9 |

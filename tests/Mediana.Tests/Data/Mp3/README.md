# MP3 frame and ID3 references

These inputs verify MPEG Layer III framing, ID3v2.3/v2.4 text and original APIC
bytes, Xing/Info frame declarations and trailing ID3v1 tags. No MP3 PCM decoder
is provided by this corpus.

| File | Rate/channels | Mode | Leading metadata |
| --- | --- | --- | --- |
| stereo-vbr-cover.mp3 | 44100/stereo | MPEG-1 VBR | ID3v2.4, PNG, Xing |
| stereo-cbr-id3v23.mp3 | 48000/stereo | MPEG-1 CBR | ID3v2.3, Info |
| mono-speech.mp3 | 16000/mono | MPEG-2 CBR | ID3v2.4, Info |
| mono-low-rate.mp3 | 8000/mono | MPEG-2.5 CBR | ID3v2.4, no Xing/Info |

`references.json` records input/source hashes, complete encoding/probe commands,
every audio packet's physical position, length, original bytes hash, timestamp
and duration in samples, complete compressed-byte hash and picture metadata/hash.
The shared managed/Native AOT verifier checks the complete extent of every input,
all ID3 text/title/artist, original picture bytes, coded sample clock, repeated
EOF and original ID3v1 fields. Media-buffer budget is 128 KiB; packets and images
retain independent leases after reader disposal and all final buffers release.
FFmpeg/ffprobe generated the fixtures and independent evidence; tests and library
runtime do not execute external media processes.

The stereo and low-rate sources are the repository's original synthetic signal
`../Waveform/music.wav`; the cover is `../Metadata/cover.png`. These original
signals/artwork and their derived files are MIT licensed.

`mono-speech.mp3` is an MP3 encoding of the existing
`Lab41-SRI-VOiCES-src-sp0307-ch127535-sg0042.wav` recording from *Voices Obscured in
Complex Environmental Settings (VOiCES)* by Lab41, In-Q-Tel and SRI International.
The source and derived MP3 are under
[Creative Commons Attribution 4.0 International](https://creativecommons.org/licenses/by/4.0/).
Changes: lossy MP3 encoding and added title/artist/track tags. See the
[source attribution](../Waveform/README.md),
[dataset documentation](https://iqtlabs.github.io/voices/Lab41-SRI-VOiCES_README/)
and [license declaration](https://registry.opendata.aws/lab41-sri-voices/).

Coded timing retains priming and padding. Xing/Info is a metadata frame and is
excluded from returned packets. A packet does not imply independent decoding:
Layer III may depend on earlier main-data bytes through its bit reservoir.
No gapless interpretation, MPEG CRC validation, free-format discovery,
resynchronization or seek claim is made.

Primary format references:
[ID3v2.3](https://id3.org/id3v2.3.0),
[ID3v2.4 structure](https://id3.org/id3v2.4.0-structure),
[ID3v2.4 frames](https://id3.org/id3v2.4.0-frames),
[ID3v1](https://id3.org/ID3v1),
[RFC 3119 section 2](https://www.rfc-editor.org/rfc/rfc3119.html#section-2).
MPEG header lengths/rate tables and Xing/Info placement/count semantics were
cross-checked against the LAME and FFmpeg format implementations. The C# reader
is an original implementation; no codec implementation was copied.

## Side information and bit-reservoir references

`syntax-references.json` extends the four files above with six `crc-<rate>-<channels>.mp3`
files: 48000, 24000 and 8000 Hz, each in mono and stereo. The additional files
were encoded with LAME 3.100, quality 2, CRC protection enabled and Xing writing
disabled. Rates are 128, 64 and 24 kbit/s respectively. Each source is one second
of original PCM16: channel c uses a 440+193*c Hz sine with amplitude 10000;
every fourth 50 ms interval whose index modulo four is one is silent. These
synthetic signals and their encodings are MIT licensed. FFmpeg 6.1.1 successfully
decodes each protected input with `-err_detect crccheck+explode`.

All 846 frames are checked in managed and Native AOT tests. The reference uses
independent textual bit extraction and absolute offsets in the complete slot
stream, with frame headers, CRC words and side information removed. It checks
all decoded side fields and assembled main-data bytes, including all four window
types, both granules of MPEG-1, low-rate scalefactor compression, and references
to earlier frames. The production reservoir retains at most 511 bytes for MPEG-1
or 255 bytes for MPEG-2/2.5, in addition to the configured current-frame space.

The side-field hash serializes little-endian int32 values for each frame:
main-data back offset, total main-data bit count, channel count, granule count,
left and right scalefactor reuse masks; then for each granule and channel:
part bit count, big-value pair count, global gain, scalefactor compression,
block type, mixed flag, three Huffman tables, three subblock gains, both region
counts, preflag, scalefactor scale and count1 table. Booleans use zero or one.
The main-data hash serializes the int32 bit count followed by its original
rounded-up bytes. The last byte can contain alignment bits outside the declared
part; bounded bit readers expose only the declared granule bits.

The internal decoder syntax validates protected header/side-info CRCs. Separate
tests corrupt every protected reference frame and require CRC rejection. This
does not change the container reader's CRC policy or establish MP3 PCM decoding.
The fixed field syntax follows ISO/IEC 11172-3 and ISO/IEC 13818-3; CRC coverage
and polynomial are also checked against the LAME encoder. MPEG-2.5 uses the
low-rate side-info layout. No external encoder or decoder runs during tests.

## Scalefactor and Huffman references

The syntax manifest also verifies every decoded scalefactor in all 846 frames.
For each granule/channel the scalefactor hash contains the consumed bit count as
little-endian int32, 39 byte values in transmission order, and 39 intensity-limit
bytes. Untransmitted trailing values are zero. MPEG-1 values preserve only the
selected previous-granule groups; low-rate values use the four prescribed
partitions. Limit 255 means no illegal intensity-position code is assigned;
zero-width low-rate partitions represent the legal intensity position zero.
Independent bit-string extraction supplies these hashes and consumed-bit totals.

`huffman-codes.tsv` contains the numeric codeword assignments from MPEG-1
Table 3-B.7, available in the [specification archive](http://www.mp3-tech.org/programmer/docs/iso11172-3.zip),
`ANNEX_AB.DOC`. Pair tuples pack x in the upper nibble and y in the lower nibble;
quadruple tables A/B use identifiers 32/33 and four one-bit magnitudes.
The code lengths exclude signs and escape extensions. The 1410 words have
complete, prefix-free trees and cover every tuple of their 17 independent tables.
Every code and length was separately cross-checked against the
[LAME numeric tables](https://github.com/lameproject/lame/blob/master/libmp3lame/tables.c).

Managed and Native AOT tests check all codewords, every bit alignment, all sign
combinations, both families of shared escape tables with each linbits width,
extension values zero/middle/maximum, exact end-of-data and a following sentinel
field. Table zero consumes no bits. Truncated pairs throw; incomplete final
count1 quadruples leave the cursor/output unchanged for the spectral reader to
treat as stuffing. Pair escape ordering is x extension, x sign, y extension,
y sign, with an unsigned extension added to magnitude 15.

`scalefactor-bands.tsv` records all 18 band-width tables for the nine sample rates,
including the final implicit band. These numeric assignments were extracted from
Tables 42–59 of Martin Sieler and Ralph Sperschneider's
[MPEG Layer3 Bitstream Syntax and Decoding, issue 1.2](https://mp3guessenc.sourceforge.io/MPEG%20Layer3%20Bitstream%20Syntax%20and%20Decoding.pdf)
and checked against ISO/IEC 11172-3 Table B.8 and ISO/IEC 13818-3 Table B.2 for
the standardized sample rates. The production tables store cumulative boundaries;
tests independently sum the published widths.

The syntax manifest includes all 576 quantized spectral values for every granule
and channel, hashed after decoding raw codewords with a separate bit-string reader.
The spectrum hash serializes the number of explicitly decoded values followed by
576 signed little-endian int32 coefficients. In all 846 frames, 2461 channel
granules contain 697050 explicitly decoded values, including 224325 nonzero values.
The rest are implicit zeros. Managed and Native AOT corpus tests compare every
coefficient through these full-stream hashes. Separate vectors cover region-table
switches, partial final count1 tuples, stuffing, spectrum capacity and the following
granule boundary.

These checks establish complete quantized spectra. Requantization, stereo
processing, filterbank synthesis and PCM comparison remain separate requirements;
there is still no public MP3 PCM decoder.

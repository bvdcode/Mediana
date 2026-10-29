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

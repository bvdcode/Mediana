# Vorbis decoding

`VorbisDecoderFactory` registers managed Vorbis I decoding in `MediaEngineBuilder`.
It accepts `StreamFormats.Vorbis` with three Xiph-laced identification, comment and
setup headers. Output is native-rate, interleaved Float32 PCM. One through eight
channels use the Vorbis speaker order; higher channel counts use discrete labels.
No native decoder or external multimedia dependency is required.

Codebook lookups retain compact scalar trees and packed quantization values rather
than expanding every vector. Both floor types, residue types 0/1/2, channel coupling,
submaps and all standard block sizes 64–8192 are reconstructed. Prepared inverse
transforms share SIMD Fourier stages with the Opus decoder. Tables, transform plans
and scratch storage are charged to the supplied allocator and reused across packets.
Frame reconstruction allocates no managed working storage. The public packet API
publishes an owned output lease per decoded frame.

The first audio packet primes overlap without producing PCM. Subsequent packets
produce `(previousBlockSize + currentBlockSize) / 4` samples per channel. A known
packet Duration trims the decoded tail on the exact native sample grid, including
the Ogg final granule. Trimming uses a leased buffer view without copying PCM.
Header packets do not change overlap state. Nominal audio-packet truncation follows
the floor and residue end-of-packet rules in the Vorbis specification; malformed
setup headers are rejected.

With `OggReader`, indexed seeking decodes and suppresses one preceding packet.
Output begins at the selected packet position; an exact sample window can be
applied afterwards. `Reset` clears overlap and preserves prepared tables, allowing
repeated seeks without reparsing setup headers. Calls on one decoder must not
overlap. Failure or cancellation requires reset or disposal. Retained frames
survive both operations; decoder disposal releases its working storage.

Independent Xiph libvorbis 1.3.7 references cover 49,152 scalar/vector codewords,
128 inverse transforms and 1,183 sequential PCM packets from 32 encoded streams.
They include 8–96 kHz, one through eight channels, four quality settings, tones,
noise, transients, coupling and block transitions. Another 192 cases check both
floor types, every residue type, nonaligned partitions and nominal truncation.
Floating comparisons require relative L2 error no greater than 0.00001 and peak
error no greater than 0.000003 times the larger of one and the reference peak.
Whole Ogg output and repeated forward/reverse seeks match independently decoded
continuous PCM, including timestamps and final sample counts.

Syntax and reconstruction follow the
[Vorbis I specification](https://www.xiph.org/vorbis/doc/Vorbis_I_spec.html).

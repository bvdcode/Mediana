# DTS Core decoding

Register `DtsDecoderFactory` with `MatroskaReaderFactory` to decode `A_DTS`
tracks through `AudioSession`. The decoder produces interleaved float32 PCM in
coded channel order. `DtsFrameHeader.GetChannelOrder()` supplies the speaker names;
dual mono remains two discrete channels. No native codec or external executable
is required at runtime.

The implementation handles mono through 5.1 Core presentations, both byte orders
of 16-bit and 14-bit packing, Huffman/block/linear quantization, adaptive prediction,
high-frequency vector coding, joint intensity coding, sum/difference channels,
perfect and non-perfect reconstruction filters, and the 64/128 LFE interpolators.
Termination frames preserve their declared padding. Optional dynamic-range and
dialog-normalization processing is not applied to the decoded samples.

DTS-HD extensions, additional channel extensions and X96 reconstruction are not
implemented. Packets containing those presentations are rejected explicitly;
additional channels or lossless residuals are never silently discarded.

Matroska indexing reads bounded packet prefixes to obtain sample counts and
opening audio metadata without reconstructing PCM. Laced frames retain individual
sample durations, and a shorter BlockDuration trims only the final frame.
`DtsStreamMetadataReader` exposes the same header facts through the common probe
API. Opening still visits each compressed frame prefix; it does not read every
complete payload.

Seeking accounts for synthesis history and inter-frame ADPCM dependencies.
Independent restart points receive bounded synthesis preroll. When prediction
continues from preceding frames, the session starts at an earlier independent
point or the original stream beginning. A late seek in such a stream can therefore
require decoding the preceding interval.

State and PCM use the caller's `MediaBufferAllocator`; published frames retain
independent leases. Reset clears predictor/filter history, and cancellation or
malformed input releases decoder storage. The CLI uses these same contracts for
probe and WAV export.

Reference: [ETSI TS 102 114 V1.5.1](https://www.etsi.org/deliver/etsi_TS/102100_102199/102114/01.05.01_60/ts_102114v010501p.pdf),
Core syntax in clause 5 and synthesis/tables in annexes C and D.

A 60-second generated-audio measurement uses Native AOT, pooled storage, complete
MKV decoding and SHA-256 hashing of PCM. Profiling attributed about 70% of decoder
time to synthesis. Reusing the shared factored 32-point cosine transform reduced
median 5.1 process time from 2.152 s to 1.219 s and CPU from 2.875 s to 1.899 s,
with approximately 24.3 MB peak RSS before and after. Stereo process time fell
from 0.806 s to 0.547 s. These are five paired fresh-process runs with alternating
order on an i5-12450H; they cover generated audio rather than a broad media corpus.

PCM export negotiates the writer's channel order through
`IAudioFrameWriterFactory.SelectFormat`. WAVE selects speaker-mask order, and the
shared `AudioFramePreparation` combines permutation and sample conversion in one
allocation. Direct decoding retains coded order. This is independent of the
input codec and requires no format-specific CLI conversion code.

# AC-3 and E-AC-3 decoding

`Ac3DecoderFactory` and `Eac3DecoderFactory` implement the shared `IAudioDecoderFactory` contract. AC-3 consumes one complete syncframe per `MediaPacket` with `StreamFormats.Ac3`. E-AC-3 accepts one or more complete syncframes of the same independent substream, totalling at most six audio blocks, with `StreamFormats.Eac3`. These self-describing representations use an empty external configuration.

MP4 uses the explicit `StreamFormats.Ac3Mp4` and `StreamFormats.Eac3Mp4` representations with the original `dac3` or `dec3` payload. Their access units contain exactly six audio blocks. The factories validate that syncframes agree with the configured sampling format. `Ac3StreamMetadataReader` and `Eac3StreamMetadataReader` inspect these configurations independently of decode support; the latter reports the main independent presentation, including declared dependent channel locations. Dependent composition, multiple independent presentations and associated-service mixing produce explicit decoder refusals.

Output is native-endian, interleaved float32 PCM. Coded channel order is preserved, with explicit labels from `Ac3FrameHeader.GetChannelOrder()`. For 5.1 this is left, centre, right, left surround, right surround, LFE. There is no implicit downmix or sample rate conversion.

The decoder synthesizes all syncframes in an access unit directly into one PCM buffer and returns one `AudioFrame`. It preserves the packet timestamp and validates presentation durations against whole samples within the combined coded audio. Clipping applies to the access unit once. `IPacketPrerollDecoder` requests one preceding packet to reconstruct transform overlap when seeking. Original startup priming remains visible; container edits and encoder padding are separate from decoding.

Both implementations validate frame length and the complete-frame CRC before decoding. Cancellation or malformed input faults the codec until `Reset` or disposal. Output already delivered to the caller remains independently owned.

## Supported tools

- AC-3 bitstream versions up to eight, 32/44.1/48 kHz, all eight base channel modes and optional LFE.
- Differential exponent strategies D15/D25/D45 and reuse.
- Normative fixed-point bit allocation, delta bit allocation and grouped/signed mantissas.
- Standard channel coupling with coupling bands, coordinates and stereo phase restoration.
- Stereo rematrixing; deterministic, channel-independent dither where signalled.
- Long and switched short inverse transforms, Kaiser-Bessel-derived window and persistent overlap.
- Line-mode dynamic range control, enabled by default. Pass `applyDynamicRangeCompression: false` to a factory to preserve the encoded dynamic range. Dialogue normalization and RF compression metadata do not change PCM gain.
- E-AC-3 independent or AC-3-converted substreams using conventional transforms and coupling, all four block counts, frame/block exponent and SNR strategies.
- E-AC-3 spectral extension with band merging, reused coordinates, noise blending and attenuation filters; it composes with stereo rematrixing and standard coupling.

E-AC-3 dependent substream composition, adaptive hybrid transforms, enhanced coupling and transient pre-noise processing are not implemented. Their presence produces a specific `NotSupportedException` before PCM is published. Reserved syntax is rejected. The sample rate code reserved by ETSI TS 102 366 is rejected rather than interpreted as a reduced-rate extension.

## Verification

The dedicated test project compares decoded PCM to independently decoded FFmpeg float32 references. It covers mono, stereo and 5.1 at the three sample rates, coupled and uncoupled spectra, all four E-AC-3 block counts, long/short transitions, and exponent reuse. Controlled inputs without dither exceed 135 dB differential SNR. Spectral-extension reference inputs cover stereo rematrixing, standard coupling, band merging and both attenuation extremes; their differential SNR exceeds 139 dB. Noise blending is checked against the banded energy envelope with a conforming independent random sequence. Dither uses an independent sequence permitted by the specification, so dithered outputs are compared by error and signal energy rather than bit identity.

Contract tests cover backpressure, timestamps, clipped presentation, preroll, retained output, cancellation, reset, corruption, allocator failure and zero additional spectral/synthesis workspace allocation during repeated decoding.

The implementation follows [ETSI TS 102 366 V1.4.1](https://www.etsi.org/deliver/etsi_ts/102300_102399/102366/01.04.01_60/ts_102366v010401p.pdf), particularly clauses 4, 6.1–6.5, 6.7, 6.9–6.10 and Annex E. No native codec or external decoder is used by the library.

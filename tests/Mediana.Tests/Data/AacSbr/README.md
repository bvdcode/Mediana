# HE-AAC references

The QMF fixtures are the complete three-frame response to a unit impulse at core
sample 13, followed by zero samples. `qmf-impulse-bands.f32le.gz` contains 96 slots
of 32 complex subbands, real and imaginary float32 components interleaved.
`qmf-impulse-out.f32le.gz` contains 6144 real output samples. Both use the
PCM16 amplitude domain; the test divides synthesis output by 32768.
`qmf-impulse-single-rate-out.f32le.gz` contains the corresponding 3072 samples
from the reference's 32-channel synthesis bank. This mode uses the lower 32
complex subbands, the downsampled prototype taps and its required modulation
phase; it does not resample full-band decoded PCM.

The independent decoder is the floating-point reference distributed with 3GPP
TS 26.410, enhanced aacPlus source dated 2009-07. Analysis uses 32 channels;
synthesis uses 64 channels without parametric stereo or low-power processing.
Its source modules are unchanged. The impulse caller clears the synthesis
scratch arrays between frames because the synthesis routine modifies them.
The original C# filterbank follows ISO/IEC 14496-3, section 4.6.18.4, with the
canonical 640-coefficient prototype from Table 4.A.89.

The tests compare every analysis component and synthesis sample, require more
than 130 dB signal-to-error ratio, check the synthesis maximum absolute error,
and verify that all allocated media buffers are released.

The mono and stereo HE-AAC fixtures contain the first 16 complete frames of
original generated signals: independent channel chirps, 7.4/9.5 kHz tones,
deterministic noise and periodic exponential bursts. The floating-point 3GPP
encoder uses an AAC-LC core at 24 kHz, SBR output at 48 kHz, 32 kbit/s mono or
48 kbit/s stereo, without parametric stereo. Each frame produces 2048 output
samples per channel, including the initial filter delay.

FFmpeg 6.1.1 supplies the complete independent float32 PCM references. Stereo
decoding uses `-threads 1 -i input.aac -f f32le output.f32le`; mono uses
`-threads 1 -i input.aac -af 'pan=mono|c0=c0' -f f32le output.f32le` to select
one channel rather than applying the gain of a stereo-to-mono downmix.
`references.json` records source and decoded PCM SHA-256 hashes. Gzip only
reduces fixture storage; the tests verify the decompressed bytes.

The shared managed/Native AOT verifier decodes every sample with implicit and
explicit SBR signaling. It requires at least 80 dB signal-to-error ratio and
maximum absolute error of 0.00025, checks the 48 kHz format, timestamps,
2048-sample durations, EOF, constant live state within a 256 KiB media-buffer
budget, and final release of every media buffer. Separate lifecycle tests cover
reset reproducibility, retained output, cancellation, truncated packets,
missing initial headers and rejection of insufficient non-origin preroll.

The `-rap.f32le.gz` references decode the complete five-frame suffix starting
at packet 11 with a fresh FFmpeg decoder, using the same options as above.
That packet carries the next independent SBR header. Tests seek forward and
backward, including immediately before and after the required two-packet
warmup, and compare every returned sample against the appropriate independent
startup reference. Repeated seeks must reproduce identical PCM and clocks.
The `.m4a` files wrap the same 16 access units unchanged in indexed MP4.

`transients-av.mp4` combines the full 46-frame stereo signal with an original
64×48 `testsrc2` pattern: 48 H.264 High pictures at 24 fps, GOP 12, no B pictures,
BT.709 limited range. FFmpeg 6.1.1 uses `libx264` for video and copies the
HE-AAC packets. The HLS test requests late, early and repeated segments,
checks that a cold late request reads only its preceding video GOP and SBR
header interval, verifies encoded audio packet clocks and decoded format,
and requires identical repeated fragments and release of all media buffers.

The `-single-rate.f32le.gz` and `-single-rate-rap.f32le.gz` references use the
same compressed access units with 24 kHz output signaling. Their complete
16-frame and five-frame startup responses contain 1024 samples per channel per
frame. FFmpeg 6.1.1 decodes an indexed MP4 with backward-compatible SBR and an
explicitly absent PS extension for mono; stereo uses forward SBR signaling.
Tests exercise both signaling forms for both channel counts, including cold
and repeated MP4/ADTS seeks. Complete mono and stereo decoding achieves
117.67 dB and 111.27 dB signal/error ratio respectively, with maximum absolute
error below 0.000006. The existing dual-rate sequences remain byte-identical.

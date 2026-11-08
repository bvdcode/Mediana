# HE-AAC references

The QMF fixtures are the complete three-frame response to a unit impulse at core
sample 13, followed by zero samples. `qmf-impulse-bands.f32le.gz` contains 96 slots
of 32 complex subbands, real and imaginary float32 components interleaved.
`qmf-impulse-out.f32le.gz` contains 6144 real output samples. Both use the
PCM16 amplitude domain; the test divides synthesis output by 32768.

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

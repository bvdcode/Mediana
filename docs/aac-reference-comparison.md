The complete AAC-LC comparison uses the floating reference source distributed
with [3GPP TS 26.410](https://www.3gpp.org/ftp/Specs/archive/26_series/26.410/26410-j00.zip).
Its AAC core is part of an enhanced AAC+ encoder. The comparison calls that core
without SBR or parametric stereo. The 120 C source/header files in the reference
encoder tree match the source archive byte for byte.

## Input and decoding checks

The reference psychoacoustic tables cover 11025, 12000, 16000, 22050 and 24000 Hz.
Its TNS parameter table also restricts the available bit rates per channel. These
limits belong to this reference implementation, rather than the AAC-LC format.
The diagnostic caller validates their intersection before opening the core.

A complete noise-signal correlation check at all five rates, in mono and stereo,
measures 1600 samples of reference delay. The dominant correlation peak exceeds
all sidelobes outside its two-sample neighborhood by at least a factor of five.
Mediana's declared delay is 1024 samples. Each decoded result is aligned using its
own delay, with complete tails retained before alignment.

Both encoders receive the same complete Float32 PCM. Five SQAM recordings and
the impulse input are explicitly resampled from 44100 to 22050 Hz; three channel
balance inputs are resampled from 48000 to 24000 Hz. Recorded speech remains at
16000 Hz. Raw bit targets are halved together with the sample rate; speech keeps
its original target. The reference bandwidth is explicitly the comparison
sample rate divided by two. The WAVE input supplied to Mediana decodes back to
the reference caller's PCM exactly.

All 20 encoded outputs from ten complete inputs, totaling 228.232375 source
seconds, decode independently. Every ADTS frame length, channel configuration,
sample rate, raw element bound and decoded sample count is checked. Mediana's
outputs also retain their exact frame ceilings and transport timestamps.
Across all 9864 access units, the largest difference between independent and
Mediana decoding is 5.07e-7 in Float32 PCM units. The checked outputs include
reference M/S syntax.

The Mediana executable is Linux x64 Native AOT. Independent decoding uses
FFmpeg 6.1.1 for development verification. The reference C caller and the complete
Mediana CLI perform different surrounding operations; this comparison supplies
no encoding-speed ranking.

## Numeric results

SNR compares every aligned source sample. It measures waveform error and is not
a listening score. Actual raw rates exclude ADTS and container overhead; the
reference uses a reservoir and constant-rate fill, while Mediana uses a frame
ceiling and can emit much smaller silent packets.

| Complete source | Rate, Hz | Raw target, kbit/s | Mediana actual, kbit/s | Reference actual, kbit/s | Mediana SNR L/R, dB | Reference SNR L/R, dB |
| --- | ---: | ---: | ---: | ---: | --- | --- |
| Castanets | 22050 | 64 | 50.09 | 64.00 | 17.84 / 17.78 | 15.13 / 14.97 |
| Harpsichord | 22050 | 64 | 51.78 | 64.00 | 20.12 / 20.37 | 20.82 / 20.79 |
| Piano | 22050 | 64 | 60.28 | 64.00 | 30.57 / 31.14 | 25.25 / 26.13 |
| Wind ensemble | 22050 | 64 | 49.47 | 64.00 | 17.48 / 17.63 | 14.24 / 15.02 |
| Pop | 22050 | 64 | 54.14 | 64.00 | 19.37 / 19.62 | 17.95 / 18.24 |
| Recorded speech, mono | 16000 | 32 | 31.25 | 32.00 | 24.19 | 20.86 |
| Impulses | 22050 | 48 | 5.68 | 48.00 | 28.51 / 26.82 | 30.52 / 29.84 |
| Silent right channel | 24000 | 48 | 47.07 | 48.85 | 50.13 / silence | 24.68 / silence |
| Quiet right channel | 24000 | 48 | 47.07 | 48.00 | 40.07 / 28.13 | 23.89 / -1.84 |
| Alternating active channels | 24000 | 48 | 47.11 | 49.16 | 32.00 / 30.54 | 21.75 / 21.12 |

For the five music recordings, analysis intervals whose mean square exceeds
1e-5 use a median 98.51% of the nominal frame budget. The complete-file averages
also include lower-energy intervals. Increasing local refinement counts from
those averages alone has no established benefit.

The 512 source-silent samples preceding the final resampled impulse onset have
these error measurements. Source magnitude throughout each interval is at most
1e-8; energy is the sum of squared sample errors.

| Channel | Mediana error energy | Reference error energy | Mediana peak error | Reference peak error |
| --- | ---: | ---: | ---: | ---: |
| Left | 1.85348e-4 | 1.52067e-6 | 0.00279231 | 0.00049360 |
| Right | 1.71451e-4 | 3.16285e-6 | 0.00331103 | 0.00088515 |

The reference has lower pre-onset error in these windows. Its final transient
access unit uses 2896 raw bits, against Mediana's 2192-bit packet under the frame
ceiling. Its different delay, window positioning, grouping, masking and tools
also prevent attributing this result to a single algorithm.

## Remaining quality work

The reference couples quiet and temporal thresholds to perceptual entropy,
minimum-SNR relaxation, scale-factor estimation and assimilation, actual
quantization, stereo decisions and reservoir state. It also uses different
windows and TNS. Its low-rate stereo preprocessing is active for the 48 kbit/s
channel-balance inputs and can alter channel separation before quantization.

These results establish a reproducible numeric reference within the core's
supported range. They establish no listening-quality ranking, isolated masking
improvement, or acceptance of 44100/48000 Hz server HLS output. Perceptual quality,
transient handling, channel balance and coding efficiency remain open for
Mediana's complete encoder.

The [window, budget and stereo comparison](aac-stereo-and-window-comparison.md)
isolates input phase, signaled windows, reference access-unit budgets and forced
M/S coding on the same complete inputs. None of those isolated substitutions
establishes a general quality improvement.

## Short-window temporal noise shaping

An isolated short-window TNS experiment was checked against the reference
arithmetic on 480 combinations of sample rate, spectrum length, coefficient
resolution, signal and amplitude. All transmitted coefficient indices matched.
Across 38,400 filtered samples, the maximum normalized difference was
1.1921e-7. This checks the filter arithmetic on identical weighted spectra;
activation, weighting and perceptual decisions require separate validation.

The complete original-rate ten-input corpus produced 20 independently decoded
outputs and 19,556 access units. Every packet budget and timestamp passed, and
Mediana's complete decoded PCM differed from the independent decoder by at most
1.9372e-7. Error energy in the four measured 512-sample silent pre-onset windows
fell by factors of 2.04–5.16. One of those windows still had a higher peak error.
On castanets, whole-channel SNR fell by 0.41 and 0.46 dB, and maximum absolute
error rose from 0.07173 to 0.15840.

This variant was rejected. The production encoder keeps its existing behavior;
filter arithmetic and improved impulse measurements do not establish acceptable
quality across real recordings. Coupled quantization and perceptual decisions
remain necessary work. The single resource-measurement pair per input does not
establish a CPU or memory improvement.

## Linked threshold and stereo model

A separate model couples per-band L/R versus M/S selection to temporal masking,
minimum-SNR adaptation, protection against spectral holes, perceptual entropy,
scale-factor seeds and exact spectral coding costs. The equations were checked
against [TS 26.403, sections 5.5.2 and 5.6.1](https://www.etsi.org/deliver/etsi_ts/126400_126499/126403/19.00.00_60/ts_126403v190000p.pdf)
and the accompanying reference source. M/S merit uses threshold-capped energy
ratios. Protection retains the original threshold when it already exceeds the
minimum-SNR ceiling and relaxes the weaker M/S component independently.

Independent vector calculations verify all twelve sample rates, mono/stereo,
three per-channel bit rates and eight spectrum patterns: 576 cases, 3456 frames
and 252576 band states. These checks cover grouped and temporal energy,
stereo selection, local peaks and valleys, effective spectral lines, protection
limits and the entropy target. They establish arithmetic correctness within
the tested model, rather than equivalence to the complete reference encoder.

The complete original-rate corpus contains 228.2323125 seconds of source PCM.
Both variants receive identical PCM, window plans and packet ceilings, with
1024-sample alignment. All ten controls reproduce the current encoder bytes.
All 20 outputs and 19556 access units pass full independent decoding and packet
checks; Mediana's Native AOT decoder differs by at most 2.385e-7 Float32 units.

| Complete source | Current SNR L/R, dB | Model SNR L/R, dB |
| --- | --- | --- |
| Castanets | 20.82 / 20.83 | 17.73 / 17.85 |
| Harpsichord | 19.84 / 20.02 | 18.38 / 18.36 |
| Piano | 41.48 / 42.02 | 30.59 / 31.53 |
| Wind ensemble | 23.70 / 23.83 | 19.95 / 20.62 |
| Pop | 24.99 / 24.74 | 22.81 / 22.96 |
| Recorded speech, mono | 24.19 | 22.11 |
| Impulses | 29.24 / 28.05 | 32.47 / 29.55 |
| Silent right channel | 60.07 / silence | 46.61 / silence |
| Quiet right channel | 46.76 / 37.38 | 42.37 / 31.12 |
| Alternating active channels | 40.12 / 37.99 | 31.24 / 37.65 |

The quiet-channel recording falls below its existing 45/36 dB signal guards.
For the right-channel impulse at source sample 45864, error energy in the
preceding 512 silent samples rises from 2.35549e-5 to 5.68902e-5, a factor of
2.415. The other three measured pre-onset intervals improve. Exact right-channel
silence is preserved in the silent-channel recording.

The same fixed-plan Native AOT harness runs sequentially on one logical CPU.
Summed CPU time over ten inputs is 25.119 seconds for the control and 20.177
seconds for the model; the largest process peak RSS is 41600/41856 KiB.
There is one measured run per variant and input, and encoded quality differs;
these values do not establish an equal-quality speed improvement.

The model was rejected. No thresholds, stereo policy or quantization seeds from
it are enabled in the library. SNR is not a listening score; matching equations
and lower errors on selected transients do not establish perceptual acceptance.
Channel balance, transient consistency and the interaction between perceptual
targets and actual quantization remain open.

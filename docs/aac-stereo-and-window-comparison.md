The AAC-LC window and stereo diagnostics separate several differences in the
[complete reference comparison](aac-reference-comparison.md). They retain the
same Mediana transform, quantization search and channel coding. The reference
core's complete psychoacoustic model is not substituted for those algorithms.

## Source phase

The two encoders have measured delays of 1024 and 1600 samples. Each of the ten
complete inputs was encoded with zero or 576 explicitly prepended silent samples.
Every prepared Float32 WAVE input round-trips to the original PCM exactly after
removing that prefix. Alignment excludes the known prefix and codec delay;
no waveform search chooses an alignment. All ten zero-prefix AAC streams are
byte-identical to the preceding complete comparison.

The 20 outputs contain 9,851 access units. Every packet ceiling, timestamp,
duration and opening window declaration is checked. Complete independent and
Mediana decoding agree within 2e-6 Float32 units. The inputs contain 228.232375
seconds of original audio, processed at both phases.

On the impulse input, whole-channel SNR changes from 28.51 / 26.82 dB to
31.59 / 30.42 dB. The final left-channel pre-onset error energy falls from
1.85348e-4 to 1.50969e-4, but its peak error rises from 0.00279231 to 0.00355313.
The quiet-channel test's louder channel also loses 1.25 dB. Input phase affects
the measurements, but it does not supply a general improvement or justify
changing the encoder's priming or adding a source prefix in production.

## Window plan and packet budget

At the 576-sample input phase, a two-by-two diagnostic selects either Mediana's
or the reference's signaled window sequence, shape and grouping, independently
of choosing nominal frame ceilings or observed reference access-unit budgets.
The complete-band spectral coding policy, quantization algorithm and L/R stereo
coding remain Mediana's in all four variants. Each variant preserves the same
complete packet count and 1600-sample effective alignment.

Reference-derived budgets may exceed Mediana's nominal frame ceiling. They are
floored at the existing complete-band syntax minimum: one trailing silent frame
in the alternating-channel input needs 296 rather than 48 bits. The reference
can declare zero active bands there. No impulse or music frame needs this floor.
This diagnostic does not implement a production bit reservoir.

All 40 outputs and 19,716 access units pass complete decoding, window and budget
checks. The independent and Mediana decoders differ by at most 1.807e-7.
All ten own-window/nominal-budget controls reproduce the entire corresponding
Native AOT CLI AAC stream byte for byte.

The final source impulse starts at sample 45,847. Error energy below sums
squared errors in the preceding 512 source-silent samples per channel.

| Windows / budget | Whole-source SNR L/R, dB | Pre-onset error energy L/R |
| --- | --- | --- |
| Own / nominal | 31.59 / 30.42 | 1.50969e-4 / 8.85208e-5 |
| Own / reference | 35.96 / 35.09 | 4.88798e-5 / 2.96191e-5 |
| Reference / nominal | 31.00 / 28.84 | 1.82021e-4 / 1.37323e-4 |
| Reference / reference | 35.12 / 34.39 | 5.70896e-5 / 3.14797e-5 |

The reference trace confirms that its transient packet spends 664 reservoir bits
and uses 2,896 raw bits. Its complete encoder's pre-onset energies are
1.52067e-6 / 3.16285e-6. Matching window positioning and packet budgets alone
does not reproduce that result. On real music, reference windows improve the
harpsichord result but reduce castanets and piano SNR; reference budgets also
slightly reduce piano and wind-ensemble SNR with the same own windows.

## Forced mid/side coding

The final impulse's two source channels have correlation above 0.999999999999.
The reference signals M/S coding in all 60 grouped bands of that transient.
Its complete behavior also includes other tools and perceptual decisions.

A separate diagnostic applies `M = (L + R) / 2` and `S = (L - R) / 2` to every
band and signals `ms_mask_present = 2`. The transform and inverse syntax follow
[3GPP TS 26.403, section 5.5.2](https://www.etsi.org/deliver/etsi_ts/126400_126499/126403/19.00.00_60/ts_126403v190000p.pdf)
and ISO/IEC 14496-3, Tables 4.5 and 4.6. One variant retains relative M/S-channel
error allocation; another gives both components the same energy normalization,
equivalent to minimizing total reconstructed L/R squared error. Neither
implements adaptive band selection or the specified perceptual thresholds.

Both variants run at each of the two previously checked budgets over all nine
complete stereo inputs: 224.832375 original source seconds, 36 outputs and
19,496 access units. Every all-band M/S declaration, packet bound and decoded
sample is checked. Complete independent and Mediana decoding differ by at most
1.789e-7. The mono speech input remains in the preceding window/budget matrix.

These whole-source SNR measurements use nominal ceilings, own windows and the
same input phase. The M/S column uses combined-channel error allocation.

| Complete source | L/R coding SNR L/R, dB | Forced M/S SNR L/R, dB |
| --- | --- | --- |
| Castanets | 18.20 / 18.05 | 18.53 / 18.35 |
| Harpsichord | 19.95 / 20.27 | 20.32 / 20.30 |
| Piano | 30.46 / 31.08 | 30.76 / 31.62 |
| Wind ensemble | 17.47 / 17.60 | 17.59 / 18.17 |
| Pop | 19.24 / 19.57 | 20.16 / 20.51 |
| Impulses | 31.59 / 30.42 | 34.42 / 25.82 |
| Silent right channel | 47.88 / undefined | 35.36 / undefined |
| Quiet right channel | 38.81 / 28.10 | 37.00 / 12.72 |
| Alternating active channels | 32.02 / 30.43 | 23.21 / 22.37 |

SNR is undefined when the source channel has zero energy. In that test, the
current L/R output remains exactly silent in the right channel. Both forced
M/S objectives introduce right-channel noise with RMS 0.000737284 and peak
0.0108871 in normalized Float32 units. An undefined SNR does not mean silence
was preserved.

All five music recordings improve in this numeric measurement, while unequal
and alternating channels regress substantially. With reference-derived budgets,
combined-error M/S reduces the final impulse's pre-onset energies to
1.32416e-5 / 1.17833e-5, still above the complete reference encoder's result.
Relative M/S-channel allocation also fails the unequal-channel cases.

## Result

No source-padding, reference-window or forced-M/S variant was adopted. These
complete checks establish that a single window or stereo substitution does not
resolve the encoder's quality gap. Per-band L/R versus M/S selection must be
coupled to thresholds, relative channel requirements and actual quantization
costs; temporal control and reservoir decisions remain part of that work.
SNR and pre-onset error are numerical diagnostics, not listening scores. These
experiments supply no CPU/RSS ranking or acceptance of 44100/48000 Hz HLS audio.

# AC-3 PCM reference inputs

The encoded clips are short, original mathematical signals; they do not contain third-party recordings. `*.pcm` files are interleaved float32 little-endian samples. Except for the mathematical AHT quantizer reference described below, they were decoded by FFmpeg 6.1.1-3ubuntu5. Each complete encoded file is decoded with:

```sh
ffmpeg -hide_banner -loglevel error -drc_scale 0 -i input.ac3 -c:a pcm_f32le -f f32le output.pcm
```

Use the same command with an `.eac3` input for E-AC-3. Ordinary clips contain four complete 1536-sample syncframes, including coded startup delay. No priming or padding is removed. The tests assert the exact decoded sample count.

| Input | Source and encoding |
|---|---|
| `mono.ac3` | 48 kHz, 96 kbps; `0.19*sin(2*PI*437*t)+0.11*sin(2*PI*5237*t)` |
| `stereo-44100.ac3` | 44.1 kHz, 192 kbps; left as above; right `0.23*sin(2*PI*619*t)+0.07*sin(2*PI*7919*t)`; coupling and rematrixing disabled |
| `coupled.ac3` | Same stereo signal at 48 kHz, 96 kbps; coupling enabled with start band 2 |
| `stereo.eac3` | Same stereo signal at 48 kHz, 192 kbps; default E-AC-3 encoding |
| `surround-32000.ac3` | 32 kHz, 256 kbps, 5.1(side); six sine channels, frequencies 317/431/571/67/739/887 Hz and amplitudes .12/.13/.14/.10/.12/.11 |
| `surround.eac3` | Same 5.1 signal at 48 kHz, 384 kbps |

The 5.1 references use FFmpeg's native FL/FR/FC/LFE/SL/SR sample order. Comparison explicitly maps these to the decoder's coded FL/FC/FR/SL/SR/LFE order.

`switched.ac3`, `enhanced-switched.eac3` and `enhanced-one/two/three.eac3` are authored bitstreams reproduced exactly by `Ac3SyntheticFrame`. They use mono spectra with 73 coefficients, exponent zero, maximum SNR offset, 16-bit mantissas (nonzero coefficients 3 = 33 and 19 = −17), and disabled dither. The switched inputs alternate long and short transforms. Enhanced inputs cover one, two, three and six audio blocks. Their checksums and independent PCM references test actual spectral decoding and synthesis, including nonzero audio, without depending on random dither equivalence.

`spectral*.eac3` inputs are authored spectral-extension streams reproduced by `Ac3SpectralFrame`. The baseband ends at coefficient 49, the extension covers 49–84, and the copy region is 25–48. Coordinates are 0.5 with master exponent 0. Signal-only blend 31 permits strict independent PCM comparison, including attenuation codes 0/31, a merged 24-bin band, stereo rematrixing, and mid-band standard coupling. `spectral-noise.eac3` uses blend 0 and is checked by spectral energy rather than random-sequence identity. Decode these single frames using an explicit `-f eac3` input format before `-i`. All references contain exactly 1536 samples per channel.

`adaptive.eac3` and `adaptive-switched.eac3` are authored six-block AHT streams reproduced by `Ac3AdaptiveFrame`. They use allocation 19, scalar gain 1 and nonzero DC/AC AHT coefficients, with fixed and alternating long/short synthesis respectively. Each independent PCM reference contains 1536 mono samples.

`adaptive-quantizers.eac3` is reproduced by `Ac3AdaptiveQuantizerFrame`: 14 syncframes, 21,504 mono samples, every VQ codebook, allocation ranges 1–18, and GAQ modes 0–3. Spectral extension limits the baseband to 49 bins and has zero envelope coordinates; nonzero quantized spectra exercise the actual AHT-to-PCM path. The accompanying PCM was calculated independently from normative codebooks and inverse-quantizer constants, the six-point DCT equation E.2.4.5, and direct 128-point complex DFT summation of 6.9.4.1. The KBD window uses Bessel I0 evaluated by 128-point trapezoidal integration. This reference uses neither the decoder's FFT nor its Bessel power series.

For this corpus, the Table E.3.4 first row from [ETSI V1.4.1](https://www.etsi.org/deliver/etsi_ts/102300_102399/102366/01.04.01_60/ts_102366v010401p.pdf) and [V1.3.1](https://www.etsi.org/deliver/etsi_ts/102300_102399/102366/01.03.01_60/ts_102366v010301p.pdf) is retained. The tested FFmpeg 6.1.1 decoder table starts with the standard's second row, and its PCM differs whenever codebook 4 is used. This difference is kept separate from the independent mathematical reference.

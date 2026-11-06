The audio waveform sample consumes supported WAV, MP3, FLAC and MP4/AAC files
through the public container registry, decoder and processing APIs. It writes
mono PCM16 RMS/peak windows as streaming JSON to standard output:

```bash
dotnet run --project samples/Mediana.Sample -c Release -- --waveform input.flac > waveform.json
```

The example registers each module explicitly. It selects the first audio stream,
reads its initial decoded format and constructs `AudioDownmix.CreateMono` for its
named speakers. Unsupported speaker roles fail explicitly. The matrix is passed
directly to `AudioWaveformReader`, followed by 400 Hz resampling with cutoff 0.97,
reflected boundaries and signed PCM16 quantization. Analysis emits 40-sample
(100 ms) windows and preserves the final partial window.

This example processes the complete decoded PCM timeline, including codec
priming and padding. It does not infer gapless trimming or apply container edit
ranges. An application selecting a range can pass `AudioTrimmer.ReadFrameAsync`
to the same reader. Cover selection, the number and placement of visual bars,
normalization, tapering and image encoding remain application choices.

## Ownership and storage

The byte source and destination stream remain borrowed. The audio session owns
decoder resources; each callback transfers its frame to the waveform reader.
The initial frame is transferred once, without a second mixing step. The reader
owns filter history and intermediate PCM, releases them at EOF and on failure,
and returns levels by value. The example checks released storage after completion.

Decoder media buffers are bounded to 1 MiB and processing media buffers to
256 KiB. These budgets cover owned media storage rather than the complete process
heap. JSON is flushed every 64 windows; neither the full recording nor the complete
window list is retained. The writer emits `startSample`, `sampleCount`, `rms` and
`peak` for each window, then complete sample/window totals after EOF.

## Independent verification

Five tracked WAVE inputs exercise silence, endpoint impulses, harmonic audio,
recorded speech and a fractional duration. Shared managed and Native AOT tests run
the actual sample and compare every window with independent PCM16 references,
using the existing 0.002 absolute RMS/peak bound. They also check exact sample and
window counts, final remainder and borrowed stream ownership/position.

Separate analytic checks cover an explicit 5.1 mono matrix, planar/interleaved
input and packet sizes 1, 3, 31 and 83. They check every quantized level, source
time and final partial window against the speaker formula. A stereo-output mixer
is rejected before reading. A mismatched input format releases its frame and
makes the reader terminal.

The complete original film's AAC 5.1 track and five complete EBU SQAM FLAC
recordings also pass through the sample. All six managed and Native AOT JSON
outputs are byte-identical. Each independent reference is mixed/resampled and
quantized separately; every RMS/peak window is compared, without display
normalization.

| Complete input | Mono 400 Hz samples | Windows | Maximum RMS error, rounded up | Maximum peak error, rounded up |
| --- | --- | --- | --- | --- |
| Original film, AAC 5.1 | 238,592 | 5,965 | 0.000177364 | 0.000488282 |
| SQAM castanets | 8,000 | 200 | 0.000002174 | 0.000030518 |
| SQAM harpsichord | 21,200 | 530 | 0.000099779 | 0.000183106 |
| SQAM piano | 36,800 | 920 | 0.000211423 | 0.000701905 |
| SQAM wind ensemble | 7,200 | 180 | 0.000095381 | 0.000122071 |
| SQAM pop | 13,200 | 330 | 0.000185090 | 0.000396729 |
| Thirty-minute generated MP3 | 720,029 | 18,001 | 0.000036586 | 0.000152588 |

The film reference uses complete FAAD2-decoded PCM, followed by an explicit mono
matrix and FFmpeg resampling. This keeps the reference separate from FFmpeg's
different handling of additional legal AAC window transitions in that recording.
The five FLAC references use FFmpeg decoding, mono mixing and resampling.
The complete MP3 reference selects FFmpeg's `+skip_manual` decoder flag to retain
the same coded extent, including priming/padding. Its Native AOT and managed JSON
outputs are also byte-identical; the final window contains 29 samples.
The resampling filters and PCM16 rounding differ between implementations.

The film's final window contains 32 samples. This preserves every resampled sample;
it differs from applications that discard a remainder when dividing a recording
into a fixed number of display bars. The sample supplies media analysis and does
not constitute an application renderer or production Cotton integration.

## Complete thirty-minute resource measurement

One Linux x64 Native AOT process reads the complete generated 48 kHz stereo MP3,
mixes, resamples, quantizes, analyzes every window and writes JSON to a file.
CPU affinity is restricted to one logical core. No build, test or other media
workload runs during the measurement. Process CPU includes user plus system time;
peak RSS comes from `wait4`.

| CPU, seconds | Elapsed, seconds | Peak RSS, MiB |
| --- | --- | --- |
| 17.408302 | 17.427399 | 18.75 |

This is a single complete pipeline measurement, without a repeated median or
FFmpeg throughput comparison. Its output is the same complete JSON checked
against the independent PCM windows above. Decoder and processing media budgets
remain fixed as recording duration grows.

The complete 720p Big Buck Bunny source was re-encoded to video-only MPEG-TS
at its original 1280x720 resolution and 24 fps. This measures the full demux,
decode, H.264 encode and transport-writing operation.

## Source and settings

The source is the [original MOV described in the HD verification](real-hd.md):
416,751,190 bytes, SHA256
`45c8bafeb9a53df7f491198d2e71529701bcf1cd51805782089fac1d32869f9b`.
Its 14,315 pictures cover 596.458333 seconds of video. Each complete decoded
result contains 19,789,056,000 visible YUV420P8 samples.

The comparison uses Linux x64 on an Intel Core i5-12450H, .NET 10.0.13 Native
AOT and FFmpeg 6.1.1-3ubuntu5 with libx264. All three timed operations ran
sequentially on logical CPU 2. FFmpeg used one decoder thread and one encoder
thread. Independent decoding, hashing and error analysis ran after all timed
operations finished. One run was measured per implementation on a shared host.

Both encoders produced progressive Constrained Baseline level 4.1 without B
pictures, with IDRs every 48 pictures. Mediana used initial QP26, a 10,000,000
bit/s packet spending target and a 20,000,000-bit packet capacity. FFmpeg used
libx264's `veryfast` preset, a 10,000,000 bit/s target and maximum rate, and a
20,000,000-bit VBV. Mediana's packet envelope and x264's VBV have different
semantics. These settings do not establish equal quality or equivalent searches.

## Complete resource and error results

CPU is user plus system time. Memory is process peak resident set size.
Transport sizes include each muxer's own MPEG-TS overhead. Aggregate PSNR
uses the summed squared error of every visible Y, U and V sample relative
to the independently decoded original, weighted by the number of samples.

| Implementation | CPU | Elapsed | Peak RSS | TS bytes | Aggregate PSNR |
| --- | ---: | ---: | ---: | ---: | ---: |
| Mediana DC prediction (`2fa8aaf`) | 1411.06 s | 1445.82 s | 117.60 MiB | 769,200,684 | 41.170 dB |
| Mediana directional prediction (`b499276`) | 1450.57 s | 1499.30 s | 118.57 MiB | 769,197,112 | 41.286 dB |
| FFmpeg / libx264 veryfast | 138.76 s | 141.22 s | 114.11 MiB | 750,701,108 | 49.213 dB |

| Implementation | Y PSNR | U PSNR | V PSNR |
| --- | ---: | ---: | ---: |
| Mediana DC prediction | 39.829 dB | 46.329 dB | 47.235 dB |
| Mediana directional prediction | 39.945 dB | 46.443 dB | 47.349 dB |
| FFmpeg / libx264 veryfast | 48.205 dB | 51.977 dB | 52.933 dB |

Directional prediction reduced aggregate mean squared error by 2.63% at nearly
the same transport size. Its observed CPU cost increased by 2.80%, and peak
RSS increased by 0.96 MiB. This change is a small error/cost tradeoff on this
source; it did not accelerate the complete operation. The directional prediction
run used 10.45 times FFmpeg's CPU and produced lower PSNR at a slightly
larger transport size. These single-run observations do not establish general
throughput, repeatability or perceptual quality.

## Independent verification

All three complete transport outputs were independently decoded. Each decoded
source picture matched its previously verified source hash before comparison
with the encoded result. The comparison covered every visible sample and
recorded exact integer squared errors for each Y/U/V plane of every picture.

All three outputs, including the libx264 result, were also decoded through the
public Native AOT consumer. Every reconstructed byte, presentation time and
duration matched the independent transport decoding. All operations reached
EOF and released all media storage. The comparison includes 19,789,056,000
visible bytes per output.

Every output packet has PTS equal to DTS, exactly one second after its source
PTS, and preserves the source frame duration. Each output contains all 14,315
pictures and 299 IDRs at the requested two-second boundaries. Canonical MP4
copies used for the native checks preserve every original VCL NAL byte.
Every consecutive packet window in each Mediana output satisfies the configured
10,000,000 bit/s refill rate and 20,000,000-bit capacity. The actual mean AVC
packet rates are 10,014.047 and 10,014.023 kbit/s before and after the change;
the initially full buffer permits this small excess over the nominal target.
This packet check does not establish Annex C HRD conformance or equivalence
to x264's VBV.

## Reproduction

```sh
taskset -c 2 mediana -i input.mov output.ts --video-codec H264 --video-only --qp 26 --video-bit-rate 10000000 --video-buffer-ms 2000 --no-color
taskset -c 2 ffmpeg -nostdin -v error -xerror -threads 1 -i input.mov -map 0:v:0 -an -c:v libx264 -threads 1 -preset veryfast -profile:v baseline -level:v 4.1 -pix_fmt yuv420p -b:v 10000000 -maxrate 10000000 -bufsize 20000000 -g 48 -keyint_min 48 -sc_threshold 0 -fps_mode passthrough -mpegts_copyts 1 -output_ts_offset 1 -muxdelay 0 -f mpegts ffmpeg.ts
```

A separate 5,000,000 bit/s, two-second-buffer attempt rejected a picture at QP51
at 442.375 seconds. It did not complete or contribute to the resource table.

## Complete fixed-QP level-burst check

The former instantaneous level guard rejected fixed QP26 near 12 seconds.
Exact access-unit and separate VCL/NAL buffer accounting (`9b76808`) lets the
entire original film complete at QP26 without rate control. Thirty-five pictures
exceed 50 Mbit/s multiplied by their own duration. The first is picture 285 at
11.875 seconds, containing a 347,000-byte VCL NAL. All access-unit size bounds
and all consecutive VCL/NAL rate windows pass independent checks. Maximum window
excess is 3,117,592 VCL bits and 3,117,672 Annex B bits, within the respective
62,500,000-bit and 75,000,000-bit capacities. Annex B counts include every AUD,
start code and repeated IDR parameter set.

| Mode | CPU | Elapsed | Peak RSS | TS bytes | Aggregate PSNR |
| --- | ---: | ---: | ---: | ---: | ---: |
| Mediana fixed QP26 | 1390.37 s | 1446.59 s | 109.50 MiB | 424,707,980 | 41.183 dB |

This was one complete Native AOT run on logical CPU 2. The actual mean AVC
packet rate is 5491.703 kbit/s. The earlier fixed-QP command could not complete,
so this is a correctness result with resource accounting. Its rate-control
settings differ from the 10 Mbit/s comparison above.

Independent checks cover every source hash, original/remuxed VCL byte, packet
PTS/DTS/duration, all 299 IDRs and every slice's unchanged QP26. Native AOT
reconstructs all 14,315 pictures and 19,789,056,000 visible samples exactly as
FFmpeg, reaches EOF and releases all media buffers. The measured Y/U/V PSNR is
40.230/43.601/44.696 dB relative to the decoded original.

```sh
taskset -c 2 mediana -i input.mov output.ts --video-codec H264 --video-only --qp 26 --no-color
```

The admission checks above are separate from the external timing verification
below. Broader source acceptance, HLS A/V encoding at HD resolution and perceptual
quality remain open.

## I_PCM selection resource check

The macroblock-size correction (`a36baba`) was measured with the same complete
source, fixed QP26, Native AOT and logical CPU 2. Independent verification ran
after the measured command completed.

| Build | CPU | Elapsed | Peak RSS | TS bytes |
| --- | ---: | ---: | ---: | ---: |
| Before (`9b76808`) | 1390.37 s | 1446.59 s | 109.50 MiB | 424,707,980 |
| After (`a49a566`) | 1393.35 s | 1442.08 s | 129.69 MiB | 424,707,980 |

The complete transport files are byte-identical, with SHA256
`709e43275e9bb4bea2eeab34774da19b36f3766037647be6f31913c07a8402c6`.
Consequently, the complete picture, timestamp, decoded-sample and quality checks
above also apply to the new output. The new operation reaches EOF with zero live
media-buffer bytes. The separate noisy QP0/QP6 references exercise I_PCM selection;
this film retains its previous QP26 output.

Observed CPU increased by 0.21%; peak RSS increased by 20.19 MiB (18.44%).
This single pair on a shared host establishes neither a throughput change nor
the cause or repeatability of the RSS increase. Zero retained media-buffer bytes
at EOF does not explain process peak RSS. The memory difference remains open.

## Memory diagnostic

A separate Native AOT consumer compares demux, decode and fixed-QP26 encoding
of the original film's first 1,440 pictures, covering 60 seconds at 1280x720.
It hashes every encoded packet and records memory counters every 240 pictures.
It does not run the transport writer or CLI logging, and disposes the source
after the selected pictures rather than reaching the film's EOF.

The four fresh processes run sequentially on logical CPU 2 in before/after/after/
before order. Both builds finish before the series, with no concurrent media,
build or test workload, forced collection or changed GC configuration.

| Build/run | CPU | Peak RSS | Managed bytes allocated during operation |
| --- | ---: | ---: | ---: |
| Before 1 | 158.139 s | 107.57 MiB | 8,487,146,160 |
| After 1 | 155.274 s | 100.50 MiB | 8,487,179,072 |
| After 2 | 151.217 s | 91.11 MiB | 8,487,160,248 |
| Before 2 | 150.552 s | 107.27 MiB | 8,486,979,792 |

All four outputs contain identical 63,879,703-byte packet payloads, with SHA256
`0fdaa5ee19cbceaf95c09e9eaf5a65b66bcae0cb89961a98202f0e493c5c1f62`.
At each processing checkpoint, the encoder and decoder account for the same
2,052,000 and 5,211,208 live media-buffer bytes in both versions. Both budgets
return to zero after disposal. GC collection counts, heap size and fragmentation
vary between runs. The
[GC heap-size counter](https://learn.microsoft.com/en-us/dotnet/api/system.gcmemoryinfo.heapsizebytes?view=net-10.0)
describes the last collection; it does not measure current process RSS.

A focused Native AOT writer allocation check creates 8,192 writers per build.
The current writer adds exactly eight allocated bytes per instance. Adding 64
checkpoints to each current writer adds no heap allocation and preserves every
output byte. This distinguishes writer state size from a per-macroblock allocation.

The minute-long diagnostic does not reproduce the full-film RSS increase.
Its results do not establish a general memory or speed improvement, nor explain
the peak in the complete CLI run. That full-film observation remains recorded
above; no allocator or GC-policy change follows from these measurements.

## External HRD timing verification

The complete fixed-QP output also passes an exact external arrival/removal
calculation under Annex C of [ITU-T H.264 V16](https://www.itu.int/rec/T-REC-H.264-202606-I/en).
The supplied timing and buffering parameters are external, as permitted by E.2.1;
the output does not acquire in-band HRD parameters or timing messages.

| Bit domain | Arrival rate | CPB capacity | Maximum fullness |
| --- | ---: | ---: | ---: |
| VCL | 50,000,000 bit/s | 62,500,000 bits | 75,462,752 / 3 bits |
| Entire Annex B byte stream | 60,000,000 bit/s | 75,000,000 bits | 28,986,944 bits |

Each domain uses one VBR schedule, a zero low-delay flag and 32-bit initial-delay
fields. Rate and capacity use scale zero, so the supplied integer values are
exactly representable. The sum of initial delay and delay offset is 112,500
ticks at 90 kHz. Initial delays at successive IDRs range from 150 to 2,806 ticks
for VCL and from 125 to 2,339 ticks for Annex B.

The calculation includes every access unit and every fullness maximum, with no
underflow or overflow. Reinitialization at each of the 299 IDRs passes for every
remaining picture: 2,141,737 access-unit checks per domain. Buffering-period gap
constraints and final empty-buffer state also pass. The calculation's own checks
include hand-derived schedules, a full buffer, and rejection of underflow,
overflow, infeasible demand and an invalid buffering-period delay.

Independent header parsing checks all 14,315 slices, frame numbers and reference
marking. The declared one-reference DPB retains each required reference and
outputs each picture at its removal time. All removal/output intervals satisfy
level 4.1. An external clock of 1/48 second represents every picture delay exactly;
the maximum CPB-removal delay is 96 ticks and every DPB-output delay is zero.

These results cover this complete fixed-QP stream with the stated external
parameters. They do not establish HRD timing for every accepted input or for the
separate packet-rate controller.

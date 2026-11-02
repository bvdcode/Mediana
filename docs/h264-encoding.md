The managed H.264 encoder accepts progressive, even-sized 8-bit planar YUV 4:2:0
and writes Constrained Baseline level 4.1 AVC. Each input becomes one access unit.
IDR pictures start independently decodable groups; P pictures reference the
preceding reconstructed picture. DTS equals PTS, with no picture reordering.

## Stream contract

```csharp
using H264Encoder encoder = H264Encoder.Create(frame.Format, timeBase, allocator,
    new H264EncoderOptions
    {
        Quantizer = 26,
        KeyFrameInterval = TimeSpan.FromSeconds(2),
        MotionSearchRange = 16
    });
if (encoder.TrySend(frame, cancellationToken) != CodecInputStatus.Accepted)
{
    throw new InvalidOperationException("Receive pending encoder output first.");
}
using MediaPacket packet = encoder.Receive(cancellationToken).Value;
```

Reference `Mediana.Codecs.H264` and use `Mediana.Codecs`, `Mediana.Codecs.H264`,
`Mediana.Core`, `Mediana.Core.Memory` and `Mediana.Core.Timing` namespaces.
`encoder.Stream` supplies the AVC configuration and clock for a container writer.
Packets use four-byte NAL lengths and preserve exact input timestamps/durations.
Input durations must be positive and exactly representable in that clock;
timestamps cannot overlap the preceding frame. Gaps remain explicit gaps.
Dimensions, pixel format, SAR and effective source color must remain constant.
Range and chroma location require explicit values. Padding repeats edge samples;
SPS crop fields retain visible dimensions.

The first frame is an IDR. `KeyFrameInterval` measures elapsed media time from
the previous IDR, including on variable-cadence input. The first input at or
after the interval starts the next group. Its default is two seconds; zero
selects an IDR for every input. Negative intervals fail creation. P pictures use
one short-term reference, sliding reference marking and modulo-16 `frame_num`.
IDR resets reference numbering and picture order. SPS/VUI declares one reference,
no reordered pictures and the configured motion-vector range. All-IDR mode
declares no retained reference.

The encoder borrows input only during `TrySend`. It retains one reconstructed
reference and at most one owned output packet. Encoding allocates a current
reconstruction while the previous reference remains live; storage is bounded by
picture dimensions and `MaximumPacketBytes`. All pictures and packets use the
caller's allocator. Receive pending output before sending more input.
`CompleteInput` releases the reference and permits draining the final packet.
`Reset` discards pending output, reference state and timing; the next accepted
frame starts a new IDR sequence. Cancellation/failure releases retained state
and requires reset or disposal. Returned packets survive both operations.
Calls and disposal must not overlap.

## Prediction and residual coding

IDR pictures select among all nine legal intra4x4 directions using reconstructed
neighbours and minimum absolute prediction error. Ties prefer the predicted
mode's shorter syntax. Chroma uses DC prediction. Only residual groups with
nonzero quantized coefficients are signalled. Selection retains coefficients for
one macroblock and reconstructs each luma block before selecting the next one.
[Complete directional prediction checks](h264-intra-prediction.md) include every
direction, boundary availability, independent pixels and measured error/size
tradeoffs. P pictures use P16x16 or
P_Skip, integer 4x4 residual transforms, per-block quantization, chroma DC
transforms and CAVLC. The reference contains reconstructed pixels, so quantization
errors do not create an encoder/decoder prediction mismatch. Deblocking is
disabled in the coded slice.

Each transformed macroblock is checked against the 3200-bit CAVLC
`macroblock_layer` limit in Annex A.3.1(n), and coefficient prefixes are bounded
by Baseline's maximum of 15. An oversized block can use I_PCM only when every
sample is nonzero, as required by Baseline. Its samples are then copied exactly,
with byte alignment and edge padding.

When coefficients exceed the prefix limit, or an oversized block contains zero
samples and cannot use I_PCM, the encoder increases that macroblock's QP until
both constraints are met. `mb_qp_delta` signals the change. The next transformed
block starts from the requested QP; blocks without residuals inherit the last
signalled QP, and PCM does not change that running value. Retrying restores only
bit-writer position and current intra-block availability. It repeats neither
motion search nor source preparation and retains no extra picture. Reconstructed
samples always use the signalled coefficients and QP.

These profile constraints appear in clause A.2.1 of
[ITU-T H.264 (06/2026)](https://www.itu.int/rec/T-REC-H.264-202606-I/en).
The bit count excludes NAL framing and emulation-prevention bytes. Final escaped
packets must still fit byte and rate budgets. Without rate control, the encoder
does not increase the whole picture's QP to satisfy a packet budget; it fails if
the final packet does not fit.

Motion search examines zero displacement and the neighboring-vector predictor,
then bounded four-direction full-pixel diamond steps. `MotionSearchRange` is the
maximum displacement in each direction, defaulting to 16 luma pixels and accepting
0 through 64. At most twice that many diamond iterations are examined. Search
minimizes summed absolute sample error across Y, U and V; ties prefer shorter
motion-difference codes. Eight half-pixel and eight quarter-pixel neighbors refine
the integer winner, followed by the original fractional neighboring-vector
predictor. Candidate displacements stay within the same configured range.
One bounded luma neighborhood shares half-sample interpolation between candidates;
quarter positions average the required clipped samples. SIMD processes 16 differences together. A candidate is
discarded when its partial luma error already exceeds the best complete error.
This search does not promise a global optimum or a rate/distortion optimum.

Prediction uses the existing decoder's border extension and chroma interpolation.
An odd full-pixel luma displacement requires fractional chroma interpolation.
Integer predictions copy complete row spans while preserving clipped borders.
An exactly matching prediction omits transform analysis. Other blocks select
their coded-block pattern from actual quantized coefficients. P_Skip is emitted
only when all residual coefficients are zero and its derived motion vector
matches the selected vector. Consecutive skips share `mb_skip_run` syntax.

An [exhaustive P/I macroblock trial](h264-mode-decision.md) measured complete
HD quality, CPU, memory and independent reconstruction. It was not adopted;
adaptive intra coding in P pictures remains open.

Without `RateControl`, `Quantizer` is the base H.264 QP from 0 through 51,
defaulting to 26. Only macroblocks requiring profile or macroblock-bit-limit
correction increase it. Level 4.1 admission checks cover dimensions, frame intervals,
access-unit compression bounds and separate VCL/NAL buffer credits. The buffers
start full and refill from exact decode-time differences, retaining fractional
bits. NAL accounting reserves Annex B delimiters and repeated IDR parameter sets.
A picture may exceed the bitrate multiplied by its own duration when the buffer
has room. Fixed QP remains fixed; an exhausted level budget rejects the picture.
These admission bounds do not supply a complete Annex C HRD timing schedule or
a target bitrate. Adaptive intra/inter decisions,
multiple references and CABAC encoding remain open.

[Fractional motion search](h264-fractional-motion.md) describes scratch storage,
independent selection checks and complete CPU/quality measurements.

Syntax and reconstruction follow
[ITU-T H.264 V15 (08/2024)](https://www.itu.int/rec/T-REC-H.264-202408-S/en),
clauses 7.3.2–5, 8.2.1.3, 8.2.4–5, 8.3–5 and 9.1–2, and Annexes A/E.
Search and forward quantization are encoder decisions; the independent decoder
checks their emitted normative representation.

Level admission follows the access-unit and VCL/NAL limits in Annex A.3.1 of
[ITU-T H.264 V16 (06/2026)](https://www.itu.int/rec/T-REC-H.264-202606-I/en).
Initial pictures and independently restartable IDRs retain the initial
compression bound; subsequent P pictures use the interval since the preceding
decode timestamp. The separate user-selected packet-rate policy below remains
independent of these level limits.

## Constrained variable packet rate

Set `H264EncoderOptions.RateControl` to enable adaptive slice quantization:

```csharp
H264EncoderOptions options = new()
{
    Quantizer = 26,
    RateControl = new H264RateControlOptions
    {
        BitRate = 1500000,
        BufferDuration = TimeSpan.FromSeconds(2)
    }
};
```

`BitRate` sets the refill rate in bits/second and the nominal spending target.
`BufferDuration` defaults to two seconds. Capacity is
`floor(BitRate * BufferDuration.TotalSeconds)` bits and must be between 64 and
62,500,000 bits; the rate must be positive and at most 50,000,000 bits/second.
These conservative maxima follow Baseline level 4.1's VCL rate/buffer limits.
Invalid settings fail creation before media allocation.

The packet budget starts full and refills from exact differences between input
PTS values, including variable cadence and gaps. Fractional bits carry between
frames without per-frame rounding loss; a gap cannot refill beyond capacity.
For every consecutive group of returned packets, the contract is:

```text
sum(packet bytes * 8) <= capacity bits + BitRate * (last PTS - first PTS)
```

This counts each AVC packet's NAL header and four-byte length prefix. It excludes
out-of-band configuration, parameter sets inserted by a writer, AUDs, transport
tables, audio and container overhead. HLS master bandwidth continues to use
measured complete transport sizes. A full initial buffer permits an initial burst;
short-file mean rate can therefore exceed `BitRate`. There is no filler or minimum
rate, so easily compressed input can remain below the nominal target. This packet
envelope does not claim full Annex C HRD conformance or supply HRD timing messages.

The controller estimates I and P costs separately from completed pictures,
normalizes them by a six-step quantizer scale and smooths each cost over successive
observations. Available credit adjusts the media-duration spending target. QP
normally changes by at most six from the last picture of the same kind. This is
an encoder model, not a perceptual quality guarantee or an optimal allocation.

If a trial exceeds the available byte budget, the encoder increases QP and
re-encodes the same input. At most eight trials are made, including QP51 when
needed. The bit writer counts complete escaped output after its storage limit
without growing beyond that limit. Each rejected reconstruction and buffer is
released before the next trial. Only an accepted picture updates the reference,
packet credit, timing and cost history. An impossible QP51 budget fails explicitly;
the encoder does not drop pictures or silently exceed the configured envelope.
`MaximumPacketBytes` and the existing level bounds also constrain every accepted
picture. Cancellation/failure and reset retain the existing ownership contract;
reset also restores initial credit and rate-model history.

## Console and HLS

```sh
mediana -i input.mp4 output.ts --video-codec H264 --video-only --qp 26
mediana -i input.mp4 output.ts --video-codec H264 --video-only --video-bit-rate 1500000 --video-buffer-ms 2000
mediana hls encode input.mp4 output --video-only --segment-ms 2000 --widths 640 320
mediana hls encode input.mp4 output --video-only --video-bit-rates 1500000,400000 --widths 640 320
```

The standalone command preserves storage dimensions/SAR unless `--width` or
`--height` bounds are supplied. YUV area filtering precedes encoding; bounds are
even and never enlarge the source. Audio omission is explicit. The command
requires an identity display transform, reports progress and publishes output
only after successful completion.

HLS decodes video once and maintains one encoder/reference per width. Each encoder
uses the requested segment duration as its media-time IDR interval. Segments begin
at actual IDRs, and all widths share those boundaries. Copied or newly encoded
audio uses the existing common timeline. See [HLS encoding](hls-encoding.md).
Standalone `--video-bit-rate` selects constrained variable rate;
`--video-buffer-ms` requires a rate and defaults to 2000 milliseconds. HLS
`--video-bit-rates` accepts comma-separated values, one per width in the same
order. Each rendition has independent credit and cost history. `--qp` is the
initial quantizer in this mode; omitting rates retains fixed-QP behavior.

## Verification

Six complete original sequences contain 480 encoded pictures and 1,074,240 visible
YUV samples. Every decoded sample matches FFmpeg 6.1.1's independent H.264 decoder
exactly. Cases cover static input, translation, chroma-only changes, scene changes,
cropped dimensions, non-square pixels, variable cadence, negative input origin,
QP 0/18/26/38/51 and repeated frame-number rollover. Managed and Native AOT also
check exact whole-TS hashes, IDR timing, PTS/DTS/durations, HLS boundaries,
completion and released allocation. The
[reference manifest](../tests/Mediana.Tests/Data/H264/TEMPORAL-ENCODING.json)
records original-signal, transport and decoded hashes and independent commands.

The original 48-picture all-IDR corpus remains checked with an explicit zero
interval. Failure tests cover invalid options, cancellation with a live reference,
reset replay, returned-packet ownership, allocation failure and packet limits.
The strict Linux x64 Native AOT runner also checks these references.

Four complete PCM sequences add 32 pictures and 111,264 visible YUV samples,
all exactly matching independent decoding. QP0 and QP6 exercise I/P PCM beside
transformed blocks, deferred P skips, cropped edges and padded input strides.
Every macroblock's RBSP length, nonzero PCM samples, slice quantizer and
macroblock QP adjustment/restoration are checked. The internal encoder reference
also matches decoded pixels, including padding, after repeated block selection.
Two further five-picture QP0 sequences switch chroma between 0 and 255 in both
directions; each sample matches independent decoding. Their transitions exercise
the coefficient overflow that previously produced an invalid Baseline packet.
The [PCM reference manifest](../tests/Mediana.Tests/Data/H264/PCM-ENCODING.json)
records decoded hashes and independent commands. A single noisy 16x16 picture
also checks exact PCM samples, successful encoding at the final packet's exact
byte limit, refusal one byte below it, and reset without retained temporary
storage. Bit-writer checks cover unaligned checkpoints, emulation prevention,
buffer growth, measured overflow and invalidated checkpoints.

Six additional rate-controlled sequences cover another 480 pictures, initial
QP0/26/51, static content, moving texture, chroma-only changes and variable-cadence
scene changes. Every visible sample matches independent decoding; the common
verifier checks every consecutive packet interval against the rate envelope and
requires adaptive valid slice QPs. See the
[rate-control manifest](../tests/Mediana.Tests/Data/H264/RATE-ENCODING.json).
Tests separately cover fractional 30000/1001 cadence without refill drift, extreme
timestamp origins and capacity-bounded gaps, reset replay, owned packets, rejected
settings, impossible budgets and exact escaped-byte counting without buffer growth.

Public managed and Native AOT HLS commands check six variants, 160 video pictures,
1,090 AAC packets and 54 segments per mode. Complete playlists and every separate
segment decode independently. Audio payload, complete PCM and every audio PTS/DTS
match the separate audio encoding reference. All 129 transport/playlist files are
byte-identical between managed and Native AOT.

## Complete before/after measurement

The following two resource comparisons use the preceding DC intra predictor.
Current directional prediction and complete corpus results are recorded
[separately](h264-intra-prediction.md).

A complete 1800.333-second, 96x80, 6 fps input contains 10,802 pictures. The
generated sequence repeats; it checks sustained processing, reference lifetime
and timing, rather than representative HD compression or throughput. Both
versions use the same Native AOT decode → QP26 AVC → MPEG-TS command, pinned to
one CPU on the same shared Linux host. Two complete runs per version use ABBA
order. No other verification workload runs alongside these measurements.

| Metric | Previous all-IDR encoder | I/P encoder, two-second IDRs |
| --- | ---: | ---: |
| Process CPU, median | 15.36 s | 16.84 s |
| Process CPU, observed range | 15.09–15.63 s | 16.69–17.00 s |
| Wall time, median | 16.58 s | 18.23 s |
| Peak RSS, median | 24.13 MiB | 24.13 MiB |
| Peak RSS, observed range | 24.00–24.25 MiB | 23.75–24.50 MiB |
| Complete TS size | 45,694,152 bytes | 30,408,624 bytes |
| Video packet rate | 121.99 kbit/s | 65.98 kbit/s |
| IDR / P pictures | 10,802 / 0 | 901 / 9,901 |
| Aggregate visible-sample MSE | 6.1775 | 7.9594 |
| Aggregate visible-sample PSNR | 40.22 dB | 39.12 dB |

The observed TS reduction is 33.45%, with 9.68% more process CPU and 9.94% more
wall time. Median peak RSS is unchanged. Video packet rate sums coded packet
bytes over media duration and excludes TS overhead. Both runs of each version
produce identical complete TS bytes. The same QP does not guarantee equal
distortion: MSE/PSNR compare all visible 8-bit Y/U/V samples against the complete
decoded source, and the I/P result has greater sample error on this input.
These results do not establish perceptual quality, a rate/distortion optimum,
HD performance or a speed comparison against another encoder.

All 10,802 output pictures, dimensions, PTS and DTS are independently checked
for each version. Native AOT decoding reproduces all 124,439,040 visible YUV
bytes from the independent TS decode exactly. The comparison uses an MP4
stream-copy remux with FFmpeg's `h264_metadata` bitstream filter to serialize
canonical SPS/PPS headers. Every original coded slice NAL is checked byte for
byte against that remux; none is re-encoded or altered. Header normalization
is part of this verification command, not an input-repair path in the library.
Long audiovisual HLS with temporal encoding, representative HD sources,
perceptual allocation and complete HRD timing/conformance remain to be checked.

## Complete rate-control measurement

A separate measurement uses the same complete 1800.333-second, 96x80, 6 fps
generated/repeated source. Three modes run twice each in ABC/CBA order, pinned
to one CPU on the same shared Linux host. Builds finish before measurements;
independent decoding follows them. The controlled mode uses
48,000 packet bits/second and a two-second, 96,000-bit buffer; QP26 initializes
the model. Both other modes use fixed QP26 and the same two-second IDR interval.

| Metric | Previous fixed-QP build | New fixed-QP mode | New controlled mode |
| --- | ---: | ---: | ---: |
| Process CPU, median | 17.62 s | 17.27 s | 17.17 s |
| Process CPU, observed range | 17.61–17.62 s | 17.01–17.54 s | 16.97–17.36 s |
| Wall time, median | 18.57 s | 17.34 s | 17.27 s |
| Peak RSS, median | 24.00 MiB | 24.25 MiB | 24.44 MiB |
| Complete TS size | 30,408,624 bytes | 30,408,624 bytes | 26,106,056 bytes |
| AVC packet rate | 65.56 kbit/s | 65.56 kbit/s | 48.03 kbit/s |
| Aggregate visible-sample MSE | 7.9594 | 7.9594 | 22.1257 |
| Aggregate visible-sample PSNR | 39.12 dB | 39.12 dB | 34.68 dB |

The fixed-QP streams are byte-identical across builds and runs. The controlled
stream is also identical across its two runs, and its TS is 14.15% smaller than
fixed QP. CPU observations are similar on this small source; this is not a
general speedup claim. Reducing the rate increases sample error, so these are
different quality points rather than an equal-quality compression comparison.

Every output preserves all 10,802 pictures, 901 IDRs, 9,901 P pictures and their
exact presentation times, with DTS equal to PTS. Complete native decoded YUV
matches independent decoding, including every one of 124,439,040 visible bytes
per output. Canonical-header remux verification also checks every coded slice
NAL unchanged, as in the preceding measurement.

An independent scan of all coded slices counts their original NAL lengths plus
four-byte packet prefixes and evaluates the envelope for every consecutive
interval. The maximum observed `sum(bits) - BitRate * elapsed media time` is
65,048 bits, below the configured 96,000-bit capacity. The 48.03 kbit/s mean
packet rate includes the permitted initial burst. Parameter sets/AUDs raise the
independently demuxed video rate to 48.45 kbit/s; transport overhead is additional.
This sustained small-source check does not replace representative HD quality,
real-source throughput or full audiovisual HLS acceptance.

A separate [complete 1280x720 re-encoding comparison](h264-hd-encoding.md)
measures the original 14,315-picture film against libx264, including CPU,
peak RSS and every visible sample's source error for those earlier builds.
The [complete 720p/360p audiovisual HLS comparison](hls-hd-encoding.md) measures
the current temporal encoder together with resizing, stereo mixing, per-variant
AAC encoding and segment output on the same original film.

## Cost of Baseline macroblock constraint checks

The coefficient-prefix and PCM-sample corrections were measured on the same
complete 1441-picture, 60.041667-second, 1280x720 raw input and
[encoding-stage settings](h264-stage-performance.md). Frozen Linux x64 Native AOT
binaries ran serially on one assigned CPU in before/after/after/before order.

| Order | Version | CPU seconds | Wall seconds | Peak RSS, KiB |
| --- | --- | ---: | ---: | ---: |
| 1 | Before | 60.598095 | 60.792558 | 52132 |
| 2 | After | 60.562368 | 61.699752 | 52432 |
| 3 | After | 61.172037 | 62.084152 | 51312 |
| 4 | Before | 62.991306 | 63.945114 | 51364 |

Every complete output retained the reference transport hash, all 1441 packets
and pictures, EOF and zero live owned media bytes. The CPU differences were
0.06% and 2.89%; these shared-host observations do not establish a stable speed
improvement. One elapsed observation increased and peak RSS varied in both
directions. This correction addresses invalid boundary output.

Fresh one-thread FFmpeg/libx264 runs of the same raw input took 13.588352
and 13.467968 CPU seconds, with peak RSS 115,104 and
114,232 KiB. The ratio of median CPU times is 4.50x. Both
FFmpeg outputs retained their independently decoded reference hash. Their better
PSNR and smaller output remain unchanged, so the ratio does not represent
equal-quality throughput. The full audiovisual HLS operation was not rerun for
this correction.

Release verification passed 2,956 managed tests, 695 H.264 tests with hardware
intrinsics disabled, all 30 packages, the strict package-only Native AOT consumer
and CLI, formatting and license-header checks. Six complete boundary sequences
match independent decoding; these checks do not establish full Annex C HRD
conformance.

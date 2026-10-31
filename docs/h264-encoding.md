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

IDR pictures use intra4x4 DC and chroma DC prediction. P pictures use P16x16 or
P_Skip, integer 4x4 residual transforms, per-block quantization, chroma DC
transforms and CAVLC. The reference contains reconstructed pixels, so quantization
errors do not create an encoder/decoder prediction mismatch. Deblocking is
disabled in the coded slice. No I_PCM blocks are used.

Motion search examines zero displacement and the neighboring-vector predictor,
then bounded four-direction full-pixel diamond steps. `MotionSearchRange` is the
maximum displacement in each direction, defaulting to 16 luma pixels and accepting
0 through 64. At most twice that many diamond iterations are examined. Search
minimizes summed absolute sample error across Y, U and V; ties prefer shorter
motion-difference codes. SIMD processes 16 differences together. A candidate is
discarded when its partial luma error already exceeds the best complete error.
This search does not promise a global optimum or a rate/distortion optimum.

Prediction uses the existing decoder's border extension and chroma interpolation.
An odd full-pixel luma displacement requires fractional chroma interpolation.
Integer predictions copy complete row spans while preserving clipped borders.
An exactly matching prediction omits transform analysis. Other blocks select
their coded-block pattern from actual quantized coefficients. P_Skip is emitted
only when all residual coefficients are zero and its derived motion vector
matches the selected vector. Consecutive skips share `mb_skip_run` syntax.

`Quantizer` is a fixed H.264 QP from 0 through 51, defaulting to 26. Dimensions,
frame intervals, access-unit size and per-frame coded bits are checked against
level 4.1 bounds. These bounds do not implement a target bitrate, VBV model or
quality guarantee. Adaptive intra/inter decisions, subpixel motion search,
multiple references, CABAC encoding and practical rate control remain open.

Syntax and reconstruction follow
[ITU-T H.264 V15 (08/2024)](https://www.itu.int/rec/T-REC-H.264-202408-S/en),
clauses 7.3.2–5, 8.2.1.3, 8.2.4–5, 8.3–5 and 9.1–2, and Annexes A/E.
Search and forward quantization are encoder decisions; the independent decoder
checks their emitted normative representation.

## Console and HLS

```sh
mediana -i input.mp4 output.ts --video-codec H264 --video-only --qp 26
mediana hls encode input.mp4 output --video-only --segment-ms 2000 --widths 640 320
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
The full managed suite has 2338 passing tests; the strict Linux x64 Native AOT
runner also passes.

Public managed and Native AOT HLS commands check six variants, 160 video pictures,
1,090 AAC packets and 54 segments per mode. Complete playlists and every separate
segment decode independently. Audio payload, complete PCM and every audio PTS/DTS
match the separate audio encoding reference. All 129 transport/playlist files are
byte-identical between managed and Native AOT.

## Complete before/after measurement

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
adaptive quality and practical target-bitrate control remain to be checked.

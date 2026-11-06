The production H.264 encoder writes `disable_deblocking_filter_idc=1` and retains
unfiltered reconstructed reference pictures. A complete sequence comparison tested
filtering each accepted reconstruction before it becomes the next reference.
The measured implementation is not enabled by default.

## Reconstruction contract

[H.264 (06/2026)](https://www.itu.int/rec/T-REC-H.264-202606-I/en), sections 7.4.3
and 8.7, specifies the filter controls and complete-picture filtering order. The
comparison writes `disable_deblocking_filter_idc=0` with zero alpha/beta offsets
in both I and P slices. It uses the existing decoder filter after complete
reconstruction and successful packet admission. Failed rate-control attempts are
discarded before filtering. Macroblock quantizers, residual counts, prediction
modes and the selected reference identity determine edge classification.

The filter modifies the accepted reconstruction in place. The existing two-picture
reference/workspace ownership is preserved; no additional full-picture copy is
introduced. Reused picture storage clears its captured reference identities.

## Complete sequence comparison

The input is the 1,441-picture, 60.041667-second 1280x720 YUV420P8 sequence used by
the [stage comparison](h264-stage-performance.md). Both Native AOT executables
use the same current source, SDK 10.0.402, 24 fps clock, six-second IDR interval,
initial QP26, 10 Mbit/s packet-rate target and two-second buffer. The reconstruction
filter and its slice controls are the experimental difference.

Processing runs sequentially on logical CPU 2 with `DOTNET_PROCESSOR_COUNT=1`.
CPU is user plus system time, and memory is process peak RSS.

| Complete encoding | Unfiltered references | Filtered references |
| --- | ---: | ---: |
| CPU seconds | 34.153469 | 40.957438 |
| Wall seconds | 35.046539 | 42.006985 |
| Peak RSS, KiB | 38,464 | 35,252 |
| MPEG-TS bytes | 78,332,832 | 78,332,268 |
| Encoded packet payload bytes | 76,055,926 | 76,054,722 |
| Aggregate PSNR, dB | 45.093622 | 45.231357 |
| Luma PSNR, dB | 44.251824 | 44.429397 |
| U PSNR, dB | 47.158936 | 47.131526 |
| V PSNR, dB | 47.909471 | 47.882227 |

Observed CPU increases by 19.92%. Aggregate PSNR improves by 0.137735 dB and
luma by 0.177573 dB; both chroma components regress slightly. Output size is
almost unchanged. This is one complete pair, so its RSS difference is not a
repeatability result and the measurements do not establish equal-quality
throughput. The existing full-film HLS comparison is unchanged.

## Independent reconstruction verification

All 1,441 accepted encoder reconstructions match SHA256 hashes of the complete
frames decoded by FFmpeg. A separate Native AOT check feeds the original encoder
configuration and packets through the public Mediana H.264 decoder contract;
all 1,441 output frames match those same independent hashes. Hash recording and
the additional decoder are excluded from the timed executables.

Both checking executions reproduce every byte of the filtered MPEG-TS output.
The unchanged control also reproduces the previous unfiltered output exactly.
Every output has 1,441 packets with exact PTS/DTS, IDRs at indices 0, 144, 288 and
subsequent multiples of 144, complete EOF and zero retained media-buffer bytes.
Each PSNR report covers all 1,441 pictures.

A separate FFmpeg TS-to-MP4 remux retains a zero byte after the PPS RBSP trailing
bits. The current strict decoder rejects that configuration for both the unchanged
and filtered outputs. The subsequent [parameter-framing inspection](h264-parameter-framing.md)
confirms that the original MPEG-TS PPS is valid and the additional zero is inside
the MP4 configuration's declared NAL length. The direct codec verification does
not prove that remuxed MP4 is accepted.

## Decision

This implementation is not adopted as the default: its small aggregate/luma
quality gain, slight chroma regression and increased observed CPU do not resolve
the current encoding-efficiency gap. Further quality work needs joint mode,
rate and distortion decisions rather than enabling this filter alone.

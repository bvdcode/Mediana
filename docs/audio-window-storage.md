`AudioTrimmer` retains the immutable storage of a complete selected PCM block.
An explicit output origin changes its timestamp through
`AudioFrame.RetainWithTimestamp`; samples, duration and the input frame remain
unchanged. Every returned frame owns an independent lease and may survive the
source session. Its original allocator counts the storage until the last lease
is released. Partial boundary blocks still copy into the supplied allocator, so
a small selection does not keep an entire input block alive.

## Complete edited AAC selection

Frozen strict Native AOT consumers process the complete AAC track of the
[full-film HLS input](hls-hd-encoding.md). Its SHA256 is
`45c8bafeb9a53df7f491198d2e71529701bcf1cd51805782089fac1d32869f9b`.
The 48 kHz track has six channels. Its explicit movie edit selects 28,630,160
samples per channel in 27,960 PCM blocks. The operation opens the container and decoder, removes edit-list
priming and padding, hashes every selected PCM byte and timestamp, reaches EOF
of the selection and releases all media buffers.

Each version runs four times, serially in before/after/after/before and reversed
order, on logical CPU 2 with one processing thread. No owned build, test or other
media workload overlaps timing. Measurements use .NET SDK 10.0.402 on Linux x64
and a shared host; CPU is user plus system time. Allocation totals cover managed
allocations during the operation, including decoding and selection.

| Resource median | Before | Retained complete blocks |
| --- | ---: | ---: |
| CPU seconds | 3.382804 | 3.266309 |
| Elapsed seconds | 3.383364 | 3.266661 |
| Managed allocated bytes | 1,495,344,284 | 804,868,120 |
| Peak RSS, KiB | 17,280 | 17,216 |

All eight complete PCM and timestamp hashes match. Source, fixture, media and
binary hashes remain unchanged throughout measurement. Allocated bytes fall by
46.18%, about 658.49 MiB, while median CPU falls by 3.44%. Peak RSS is essentially
unchanged. These results cover decoded audio selection; they do not measure a
video encoder or establish an HLS throughput improvement.

## Ownership and boundary checks

Managed and Native AOT checks cover all five PCM representations, interleaved
and planar storage, independent disposal, exact rational retiming and explicitly
unknown timestamps. Whole-block selection succeeds with a one-byte destination
budget and preserves the original memory views. Partial edges still enforce the
destination budget. Existing checks cover both selection boundaries, short EOF,
continuous clocks, source failure and cancellation after input ownership transfers.
Real edited AAC selections remain checked against independently decoded PCM.

Independent audiovisual intervals require correct decoder restart and timestamp
coordination.

# Complete-operation allocation cost

A diagnostic Native AOT consumer measures buffer allocation intervals and GC
pauses over complete H.264 decoding and encoding. Its product baseline matches
`979544e`, including checked synchronous spans and compact interpolation scratch.
No buffer pool or codec policy changes are enabled.

## Scope

The input and independent references are the same complete 1,441-picture,
60.041667-second 1280x720 input used by the
[stage comparison](h264-stage-performance.md#input-and-measurement).
Decoding writes all 1,992,038,400 raw YUV bytes. Encoding reads those independent
raw samples and writes the exact 78,332,832-byte MPEG-TS reference.

Ordinary/profile/profile/ordinary operations run serially for each stage on
logical CPU 2 of the same Linux x64 host, using SDK 10.0.402 and
`DOTNET_PROCESSOR_COUNT=1`. No owned build, test or other media operation overlaps
timing. Both versions use identical external process and thread observations.
The table reports medians of two complete processes per version. Process CPU is
user plus system time; RSS is process peak resident memory.

| Resource | Decode ordinary | Decode diagnostic | Encode ordinary | Encode diagnostic |
| --- | ---: | ---: | ---: | ---: |
| Process CPU, s | 30.090751 | 29.664533 | 57.451989 | 58.244435 |
| Process elapsed, s | 31.960941 | 30.413267 | 58.835294 | 60.052023 |
| Peak RSS, KiB | 55,232 | 56,762 | 35,432 | 35,358 |

These differences are observations of instrumentation and shared-host variation,
not a product acceleration. No new FFmpeg or complete-film HLS ratio is measured.

## Allocation intervals and GC pauses

The diagnostic copy measures entry-to-exit elapsed intervals of
`MediaBufferAllocator.Allocate`, including budget checks, array and owner
creation, and GC pauses encountered during the call. It also samples
[`GC.GetTotalPauseDuration`](https://learn.microsoft.com/en-us/dotnet/api/system.gc.gettotalpauseduration?view=net-10.0)
at the operation boundaries. That API reports paused elapsed time, not GC CPU.
All four diagnostic operations report workstation GC and `Interactive` latency.

| Counter | Complete decode | Complete encode |
| --- | ---: | ---: |
| Buffer allocation calls | 4,329 | 10,056 |
| Requested buffer bytes | 3,347,134,762 | 2,279,419,269 |
| Sum of allocation elapsed intervals, s | 0.255135 | 0.261802 |
| GC paused elapsed time, s | 0.149159 | 0.157826 |
| Measured operation elapsed, s | 30.405494 | 60.044401 |

Allocation intervals occupy 0.84% of measured decoding elapsed time and 0.44%
of encoding elapsed time in this batch. GC pauses occupy 0.49% and 0.26%.
The intervals can overlap and must not be added. Allocation intervals do not
cover all runtime allocation or concurrent GC work outside allocator calls;
they are not attributed CPU percentages.

An external observer reads only the owned consumer's Linux thread CPU counters
at 20 ms intervals. Observed background-GC thread maxima are 0.21/0.22 CPU
seconds for decoding and 0.19/0.17 for encoding. These are lower bounds: short
thread lifetimes and final polling intervals can be missed. They are not an
exact GC CPU total or an upper bound on all collection work.

The measured allocator intervals and GC pauses do not support treating buffer
pooling as the primary remedy for the current multi-fold CPU gap. This does not
establish the cost of every allocation or identify the largest remaining compute
kernel. A pool would still require correct final-reference ownership, explicit
idle-memory accounting and operation lifetime management.

All eight complete operations match their independent output references, reach
EOF and release all owned media storage. Product sources, fixtures and consumer
hashes remain unchanged during measurement. The instrumented consumer publishes
with Native AOT and warnings treated as errors. Counters stay in the diagnostic
copy; product buffer ownership and allocation behavior remain unchanged.

Complete raw-video encoding at `452d803` was profiled to distinguish coefficient
writing from inverse transforms and reconstructed-pixel writes. The diagnostic
consumer processes the same 1,441-picture input and settings as the
[stage comparison](h264-stage-performance.md#input-and-measurement).

## Diagnostic work distribution

The strict Native AOT diagnostic runs on one assigned CPU. Every output byte
matches the independently decoded MPEG-TS reference, with EOF and zero retained
media storage. Source hashes remain unchanged.

| Timed region | Calls | Instrumented elapsed seconds |
| --- | ---: | ---: |
| Picture encoding | 1,441 | 53.301463 |
| Inter-picture encoding | 1,430 | 51.944159 |
| Intra-picture encoding | 11 | 1.159524 |
| Inter luma writing | 5,148,000 | 17.172375 |
| Chroma writing | 5,187,600 | 5.377941 |
| CAVLC residual writing | 67,204,034 | 8.396480 |
| Inverse 4x4 transform | 43,076,450 | 3.543609 |
| Reconstructed block writes | 43,076,450 | 2.307647 |
| Prediction block copies | 43,076,450 | 0.939168 |

These nested elapsed measurements include timer overhead and effects on
inlining. They are not CPU percentages and must not be summed. The complete
diagnostic process uses 54.139285 CPU seconds, 54.457094 elapsed seconds and
52.77 MiB peak RSS. Uninstrumented measurements determine whether a change is
useful.

## Direct CAVLC level experiment

The prefix and suffix rules in [ITU-T H.264](https://www.itu.int/rec/T-REC-H.264-202606-I/en),
clause 9.2.2, permit direct derivation of a complete level codeword. A diagnostic
variant replaces prefix enumeration and three bit-writer calls with a direct
calculation and one write. A second variant removes its intermediate writer
method and inlines codeword arithmetic; Native AOT disassembly confirms the
removed calls.

Every possible 16-bit level-code value was checked against independent prefix
interval enumeration at suffix lengths zero through six. Every representable
word was also written and parsed at all eight bit alignments. All complete
encoded transports match the original 78,332,832-byte reference exactly.

Frozen binaries run serially on logical CPU 2 with one processing thread and no
overlapping build, test or media workload. Each observation encodes the complete
60.041667 seconds and reaches EOF. These are shared-host measurements.

| Variant and order | Before CPU median, s | Variant CPU median, s | CPU reduction |
| --- | ---: | ---: | ---: |
| Direct word, before/after/after/before | 44.257118 | 42.764489 | 3.37% |
| Direct word, after/before/before/after | 41.476862 | 41.470086 | 0.02% |
| Inlined direct word, before/after/after/before | 41.583689 | 40.964053 | 1.49% |

The first individual pair appeared 11.15% faster, but the reversed series did
not confirm that gain. The final variant's small observed reduction does not
justify an added codeword abstraction and inlining requirement for the current
performance work. Neither variant was adopted. The existing CAVLC implementation
and its validated encoding output remain unchanged.

## Next structural investigation

Mediana currently prepares horizontal, vertical and diagonal half-sample data
for each motion-search neighborhood. The
[x264 frame filter](https://github.com/mirror/x264/blob/master/common/mc.c)
prepares reusable filtered reference planes. Reusing prepared reference data in
Mediana remains open and requires independent boundary/rounding checks, explicit
memory ownership and a complete CPU/RSS comparison. No speed or memory benefit
is established by this investigation.

# MP4 recovery groups

`Mp4TrackInfo.RecoveryGroups` preserves supported `roll` and `prol` sample-group
declarations from non-fragmented MP4/MOV files. They describe decoder recovery
history in encoded track samples, independently of media edits and encoder delay.
The [MP4 Registration Authority](https://mp4ra.org/registered-types/sample-groups)
registers the grouping identifiers.

```csharp
Mp4SampleRecoveryGroup group = track.RecoveryGroups.Single(
    group => group.Kind == Mp4RecoveryKind.Roll);
short? distance = group.GetDistance(packetIndex);
```

`packetIndex` is zero-based in decode order. A negative distance requires that
many preceding encoded samples to recover at the marked sample. A positive
distance marks recovery after the indicated number of following samples. Zero
is prohibited; `null` means that no group applies at this index.

`Mp4RecoveryKind.Roll` represents `roll`. For audio, individual samples can be
decoded independently but need the declared preceding history for correct output.
`Mp4RecoveryKind.PreRoll` represents `prol`: the stream also requires a sync sample
at or before the position obtained after backing up by the declared distance.
A declaration alone does not validate the compressed data or establish that a
decoder supports the required restart.

Description versions 0–2 and mapping versions 0–1 are read with explicit flags
and length validation. Fixed and variable description lengths must contain one
signed 16-bit distance. Mapping index zero explicitly means no group; unmapped
trailing samples use the description's default group when present. Duplicate
recognized groups, invalid description indices, zero distances and runs exceeding
the track sample count fail. Nonzero grouping parameters and newer versions fail
explicitly. Other grouping types retain their existing treatment.

Metadata stores mapping runs rather than expanding a distance array for every
sample. Queries use binary search over those runs. Declared entry counts are
checked against box extents and configured limits before allocating arrays.
Recovery objects remain valid after reader disposal without retaining encoded
input buffers. Movie-time stream translation preserves the same declarations.

Three real AAC inputs independently declare a version-one `roll` description of
−1, covering all 19, 17 and 19 encoded samples respectively. Managed and Native
AOT checks inspect every mapped sample, access encoded packets and retain the
recovery metadata after disposing the reader. Additional checks cover signed
extremes, both group kinds, defaults, explicit no-group runs, empty tables,
truncated payloads, invalid references, lengths, versions, flags and cancellation.

Five variants of the real mono AAC file exercise every supported description
version with fixed or variable entry lengths, both group kinds, explicit no-group
runs and trailing defaults. All 190 Native AOT distance queries match independently
constructed expectations. Independent packet inspection confirms unchanged
payload SHA-256, PTS and DTS for all 19 audio packets in every variant.

These declarations do not change `AudioSession` into a seekable session.
Codec preroll, exact media/movie-time selection and handling of synthesized noise
remain separate from reading the container metadata. AAC encoder priming and
padding are also separate; see [audio/video intervals](audiovisual-windows.md).

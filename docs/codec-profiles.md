# Codec profile metadata

`AudioStreamMetadata.CodecProfile` and
`VideoStreamConfiguration.CodecProfile` expose a common `CodecProfileInfo` with
`Name` and nullable `Level`. A missing description means the profile has not been
established. Levels are codec-specific names, including values such as `1b`,
rather than floating-point numbers.

The existing metadata-reader registry supplies these facts during `ProbeAsync`.
It reads opening codec configurations without decoding frames or scanning media
packets. No decoder registration is required. Metadata remains usable after the
source is disposed.

For H.264, each opening SPS retains its own profile and level. The common view
includes Baseline/Main/Extended, the High families and the other profile IDs
accepted by the SPS reader. Constraint flags distinguish Constrained Baseline,
Progressive High, Constrained High, Progressive High 10 and the High Intra
variants. Native `ProfileIdc`, `ConstraintFlags` and `LevelIdc` remain available
on `H264SequenceMetadata`.

Level 1b follows both H.264 representations: `level_idc=11` with
`constraint_set3_flag=1` for Baseline/Main/Extended, and `level_idc=9` for the
other recognized profiles. Unknown level codes have no common level name; the
original `LevelIdc` is preserved. The interpretation follows
[ITU-T H.264 (08/2024)](https://www.itu.int/rec/recommendation.asp?lang=en&parent=T-REC-H.264-202408-S),
clauses 7.4.2.1.1, A.2 and A.3 and the corresponding extension profile definitions.

No opening SPS is selected as active by default. Profile names describe header
declarations; they do not verify complete stream conformance or establish that a
registered decoder supports that configuration. Other profiles compatible with
the same constraints are not enumerated, and later in-band changes are not
scanned.

The AAC reader reports `LC` after validating a supported AAC-LC
AudioSpecificConfig. It does not infer an AAC level from the sampling rate,
channel count or bit rate. PCM, FLAC and MPEG Layer III readers leave the common
profile absent.

The CLI `probe` prints known profiles and levels beside the corresponding audio
stream or video configuration. Unknown levels are omitted. Profile text uses
the same JSON escaping as other string metadata so control characters cannot
insert console lines or escape sequences.

Regression coverage includes 37 profile/constraint/level combinations, two
distinct opening SPS alternatives, unknown levels, source ownership and read
ranges, and console escaping. Eleven independent AVC file references verify
common names and levels together with the original numeric codes. The audio
header corpus verifies AAC-LC declarations and absence of invented profiles for
the other audio codecs.

All 2,722 managed tests and the complete strict Linux x64 Native AOT runner pass.
Fifteen complete files were also probed through both CLI builds: all 30 runs
matched the independent profile references. Existing metadata for the combined
AVC/AAC input remained identical to version 0.1.12 apart from the added profile
and level lines.

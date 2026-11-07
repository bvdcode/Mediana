# Indexed video intervals

`IIndexedPacketTimingReader` exposes presentation time, decode time, packet duration
and the container sync flag without reading compressed payload or changing packet
cursors. MP4 and Matroska implement it. Unknown fields remain null. Container sync
flags alone do not establish codec restart safety.

`VideoSegmentPlanner.Create` and `ContainerSession.PlanVideoSegments` produce
presentation-aligned encoding intervals from indexed complete video access units.
The planner sorts timestamps in presentation order, including B-picture reordering.
Cuts select the first unit at or after successive target multiples from the initial
presentation time. They do not repeatedly add a rounded segment duration. Targets
shorter than a frame produce one interval per unit, rather than empty intervals.
The final interval uses the last unit's known presentation duration. Missing final
duration or duplicate/missing presentation timestamps are rejected.

```csharp
await using ContainerSession input = await engine.OpenContainerAsync(source);
using VideoSegmentPlan plan = input.PlanVideoSegments(videoStreamIndex,
    new MediaDuration(6, new TimeBase(1, 1)), workspaceAllocator);

MediaTimeRange interval = plan[requestedSegment];
```

`MediaTimeRange` is an exact half-open interval. It allows negative presentation
times and does not change the timeline origin. Its bounds share one clock, and its
duration is derived from them. `TimeBase.Common` represents complete input clocks;
`MediaTimestamp.ReduceClock` removes unused denominator factors from an actual time.
The planner derives its common clock from actual indexed times and final duration,
preserving exact values without retaining unnecessarily fine source precision.

Both sorting workspace and the returned `VideoSegmentPlan` belong to the supplied
allocator. Dispose the plan to release its storage. Copied interval values survive
plan and source disposal. Planning retains clock metadata, not compressed media or
decoded pixels. Its cost depends on the packet index size.

`AvcHlsTranscodeSession.Create` has a target-duration overload that uses this planner.
Its encoded output uses the plan's exact common clock. The explicit-window overload
retains the source stream clock. Both paths validate windows and codec configuration
without reading source payload. A segment request independently seeks to a validated
decoder restart, discards preroll and encodes the selected interval. Resizing and
precision preparation use `VideoPreparation`.

These intervals are for encoding. Compressed copy additionally needs validated
codec access points and dependency closure. The on-demand AVC session currently
emits one video track; audiovisual interval orchestration remains separate work.

## Verification

Managed tests cover 24, 30000/1001 and 60000/1001 frames per second, variable intervals,
reordering, negative offsets, short tails, nanosecond declarations with fractional
duration, sub-frame targets, cancellation, allocation failure and owned-plan lifetime.
Real MP4 and Matroska tests compare every indexed clock with sequential packets and
verify that metadata queries perform no payload reads. On-demand HLS tests request
segments in reverse/repeated order, compare complete independent decoded pixels and
clocks, and verify bounded source reads, cancellation and retry. The same indexed
planning/HLS path is exercised by the Native AOT suite.

Packet duration and presentation order remain separate concepts. Matroska declares
block/default durations and defines missing block duration through subsequent display
order when no default exists; see [RFC 9559](https://www.rfc-editor.org/rfc/rfc9559.html).
Fragmented HLS requires consistent track clocks and independently decodable segment
boundaries; see [RFC 8216](https://www.rfc-editor.org/rfc/rfc8216.html).

# Continuous audio HLS

`AudioHlsWriter` writes one continuous AAC-LC or MPEG-1/2 Layer III track into
MPEG-TS files and a VOD media playlist. It accepts encoded packets, so applications
can copy a supported source track or feed one encoder throughout the presentation.
It does not require a source container or retain the recording in memory.

```csharp
await using AudioHlsWriter writer = AudioHlsWriter.Create(
    audioStream, openSegment, allocator,
    new HlsWriteOptions { TargetDuration = new(6, new(1, 1)) });

// Feed every encoded access unit in continuous presentation order.
await writer.WritePacketAsync(packet, cancellationToken);

// After the last packet:
await writer.CompleteAsync(cancellationToken);
string playlist = writer.CreateVodPlaylist(number => $"audio-{number}.ts");
```

`openSegment` has type `Func<int, CancellationToken, ValueTask<Stream>>`. It
receives consecutive zero-based segment numbers and must return a different
writable stream for each file. The writer owns those destinations and disposes
each after finishing it. It borrows packets and the allocator; callers release
their packets after each write. Cancellation or a failed operation faults the
presentation, which must then be disposed. Concurrent operations are rejected.
Completed segment metadata and playlist creation remain available after disposal.

## Boundaries and duration

Packets must belong to the configured track and have known, contiguous PTS = DTS.
The stream clock must represent complete coded frames exactly. Nominal segment
boundaries occur at multiples of `TargetDuration` from the first coded PTS. The
first packet at or after each boundary starts the next file. Every boundary uses
that same origin, so packet rounding does not accumulate over successive segments.
Segments are nonempty and preserve every access unit.

Segment duration follows the complete coded frame count from the codec
configuration. A source container can shorten its final packet's declared
presentation duration; copying that value would understate the audio actually
written to MPEG-TS. Container presentation trimming is therefore not applied to
the coded segment extent. Encoder priming and final padding remain in the stream.

The shared output component handles destination ownership, transport continuation,
completion and byte accounting for both `AudioHlsWriter` and `AvcHlsWriter`.
Audio boundary selection remains separate from AVC IDR selection. Each file starts
with PAT/PMT; clocks, PID counters and PCR state continue across files. These rules
follow [RFC 8216, section 3](https://www.rfc-editor.org/rfc/rfc8216.html#section-3)
and its [packet-boundary requirements](https://www.rfc-editor.org/rfc/rfc8216.html#section-6.2.1).

The playlist does not declare `EXT-X-INDEPENDENT-SEGMENTS`: AAC overlap and MP3
reservoir history can require preceding packets. `CreateRendition` supplies measured
metadata for [master-playlist audio groups](hls-audio-groups.md). Applications
coordinate the associated video and audio presentation timelines.

## Verification

Managed and Linux x64 package-only Native AOT tests check the fixed boundary grid
against arbitrary-precision rational arithmetic. They verify every compressed
packet, timestamp, segment duration, byte count, transport table, counter and PCR
cadence. Further tests cover invalid timing, shortened or absent packet durations,
segment limits, concurrent calls, cancellation and destination ownership.

A public package consumer exports the following complete audio presentations.
FFmpeg independently probes every packet and decodes both the media playlist and
the concatenated files. Every Float32 PCM byte matches the continuous reference:

| Audio | Sample rate | Channels | Packets | Segments | Decoded samples per channel |
| --- | ---: | ---: | ---: | ---: | ---: |
| Copied AAC-LC | 48,000 Hz | 1 | 76 | 7 | 77,824 |
| Copied MPEG-1 Layer III | 44,100 Hz | 2 | 277 | 29 | 319,104 |
| Copied MPEG-2 Layer III | 22,050 Hz | 1 | 278 | 29 | 160,128 |
| Encoded AAC-LC | 48,000 Hz | 2 | 564 | 3 | 577,536 |

The encoded case selects 12 seconds from a real 5.1 movie, mixes to stereo and
uses one AAC encoder at 128 kbit/s. It selects 576,000 PCM samples per channel;
1,024 priming samples and 512 padding samples account for the coded total. With a
six-second target, the first-coded-PTS grid produces 282, 281 and 1 packets. The
final short segment contains the remaining coded tail. Packet payloads also
match an uninterrupted transport output from that same encoder. Media allocations
return to zero after completion and disposal.

The full validation passes 3,149 managed tests, all 30 package checks, the strict
Native AOT test executable and Native AOT CLI compilation. Release compilation
reports zero warnings and errors; formatting and license-header checks pass.
This verification establishes audio media-playlist continuity. Master audio-group
metadata is supported; independently requested video playback, common VOD
presentation boundaries and application playback acceptance remain open. See
[independently encoded interval boundaries](audiovisual-windows.md#independently-encoded-interval-boundaries).

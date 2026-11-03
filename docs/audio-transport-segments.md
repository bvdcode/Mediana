# Audio transport segments

`MpegTsWriter.BeginSegmentAsync` and `CaptureSegmentState` accept a program with
one AAC-LC or MPEG-1/2 Layer III track. Audio boundaries fall between complete
coded access units. The same APIs continue to accept exactly one AVC track with
optional audio; those segments must begin with the AVC track's IDR access unit.

`BeginSegmentAsync` flushes the preceding borrowed destination and switches to a
different writable stream. The next packet belongs to the program clock track.
PAT and PMT begin each new file. PID continuity counters, PCR state, timestamp
offsets, per-track ordering and the configured gap limit survive the boundary.
Empty or pending segments cannot be captured, completed or skipped. Destinations
remain borrowed; failure faults the writer and releases its media storage.

`CaptureSegmentState` produces immutable configuration and transport state without
I/O or media-buffer retention. `MpegTsWriter.ResumeSegment` restores it into an
independent writer and allocator after the original writer has been disposed.
Reusing the same snapshot and packets produces the same segment bytes regardless
of request order. The caller supplies packet ranges and the continuous coded
audio sequence; the snapshot contains no compressed media or decoder history.

These rules follow the continuous bitstream, timestamp and continuity-counter
requirements of [RFC 8216, section 3](https://www.rfc-editor.org/rfc/rfc8216.html#section-3).
Its [transport segment rules](https://www.rfc-editor.org/rfc/rfc8216.html#section-3.2)
require one MPEG-2 program and program tables; they do not require a video track.

## Verification

Managed and Linux x64 Native AOT tests divide three real MP4 audio tracks into
three nonempty windows. Restored segments requested as 2, 0, 1, 2 match continuous
output byte for byte. Every compressed access unit, PTS/DTS, program table,
continuity counter and PCR cadence is checked both within each file and across
the concatenated transport sequence. Ownership tests also cover snapshots after
reader/writer/source disposal, rejected timestamps, empty output and pending
segment rejection. All allocator-owned media bytes are released.

A separate Native AOT package consumer using public APIs exports the same audio
tracks. FFmpeg independently probes every packet and decodes each complete
concatenated transport sequence. Every decoded Float32 PCM byte matches decoding
all coded samples from the source track with movie edit trimming disabled:

| Audio | Sample rate | Channels | Packet windows | Decoded samples per channel |
| --- | ---: | ---: | --- | ---: |
| AAC-LC | 48,000 Hz | 1 | 25 / 25 / 26 | 77,824 |
| MPEG-1 Layer III | 44,100 Hz | 2 | 92 / 92 / 93 | 319,104 |
| MPEG-2 Layer III | 22,050 Hz | 1 | 92 / 93 / 93 | 160,128 |

The complete validation passes 3,124 managed tests, all 30 package checks and the
full strict Native AOT test executable. Release compilation reports no warnings
or errors. Formatting and license-header checks pass.

## Continuous audio preparation

Partitioning one encoded stream preserves its audio history across segments.
Decoding an individual segment with a fresh decoder is a separate operation:
AAC overlap and MP3 reservoir history can depend on preceding packets. Transport
state does not provide that codec history, remove original encoder priming or
padding, or establish `EXT-X-INDEPENDENT-SEGMENTS`.

The audiovisual interval sample still starts a fresh AAC encoder for each
request. These transport APIs provide a building block for prepared continuous
audio renditions; they do not make separately encoded intervals seamless.
[`AudioHlsWriter`](audio-hls.md) now composes that transport lifecycle with a fixed
audio segment grid and a VOD media playlist. A public Native AOT consumer verifies
one continuous AAC encoder across a real 12-second preparation. Master audio
groups, their integration with independently requested video and application
playback verification remain open. See the measured
[audiovisual interval boundaries](audiovisual-windows.md#independently-encoded-interval-boundaries).

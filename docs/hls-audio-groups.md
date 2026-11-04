# HLS audio groups

`HlsMasterPlaylist.Create(variants, audio)` associates video-only media playlists
with external AAC-LC or MPEG-1/2 Layer III audio playlists. `HlsAudioRendition`
describes a named member of an audio group, its codec, channel count and measured
transport bitrates. Completed `AudioHlsWriter` output supplies those measurements:

```csharp
HlsAudioRendition audio = audioWriter.CreateRendition(
    "audio/index.m3u8", "main-audio", "English") with
{
    Language = "en",
    IsDefault = true,
    AutoSelect = true
};
HlsVariant video = videoWriter.CreateVariant("video/index.m3u8", 640, 360) with
{
    AudioGroupId = audio.GroupId
};
string master = HlsMasterPlaylist.Create([video], [audio]);
```

Both writers must have completed successfully. The video writer in this example
contains only video. `Language`, when supplied, is a caller-provided BCP 47 tag.
Quoted attributes reject quotes and control characters; URI validation also
rejects malformed or oversized references. Unicode names and commas inside names
are preserved as quoted text.

## Selection and measurements

The master writes one `EXT-X-MEDIA:TYPE=AUDIO` entry per member. Group names are
unique, and a group can contain at most one default member. A default member must
also permit automatic selection. Multiple groups contain corresponding named
members with matching language and selection attributes; their playlist URIs,
channel counts, codecs and measured rates can differ.

Each variant's `CODECS` combines its AVC identifier with every distinct codec in
the associated audio group. Its peak bitrate is the video peak plus the largest
audio peak; its average bitrate uses the independently largest audio average.
The two maxima can come from different members. Arithmetic overflow is rejected.
The same video playlist can reference different groups through separate variant
entries. A duplicate playlist/group pair is rejected.

These rules implement the rendition and aggregate-bandwidth requirements in
[RFC 8216, sections 4.3.4.1–4.3.4.2](https://www.rfc-editor.org/rfc/rfc8216.html#section-4.3.4.1).
Audio rates include actual TS overhead. `CreateRendition` measures the complete
output with the same contiguous-duration peak window used for video variants.

## Shared target duration and timeline

`HlsWriteOptions.PlaylistTargetDurationSeconds` sets an explicit common
`EXT-X-TARGETDURATION` for audio and AVC writers. For example, assign `6` to both
writers for a six-second presentation grid. Every completed segment's duration,
rounded to the nearest integer with halves rounded upward, must fit the declared
target. A 6.016-second audio segment fits a target of six; a 1.5-second segment
does not fit a target of one. Without an explicit target, each playlist uses its
measured maximum segment-duration ceiling.

For VOD, the common target can also be chosen after every writer completes.
`CreateVodPlaylist`, `CreateVariant` and `CreateRendition` have overloads with
an explicit `targetDurationSeconds`. That argument replaces the writer's configured
value; `null` selects the measured ceiling. Pass the same common value to playlist
creation and bitrate measurement. The overloads leave segment contents, durations
and the writer's configuration unchanged.

Peak bitrate measurement uses this advertised target duration too. Changing it
changes the set of eligible measurement windows; writing a different target
after measuring bandwidth would produce inconsistent metadata.

The master formatter does no media I/O. Applications supply matching content,
media timestamps, transport offsets, target durations and VOD presentation ranges,
as required by [RFC 8216, section 6.2.4](https://www.rfc-editor.org/rfc/rfc8216.html#section-6.2.4).
The individual writers retain their original packet clocks and complete coded
audio extents. Encoder priming can place the first audio packet before the first
video picture. Group association alone does not resolve that presentation boundary.

## Verification and remaining work

Managed and Linux x64 Native AOT checks cover selection metadata, all three audio
codec identifiers, distinct codec unions, independently maximal rates, repeated
video references with different audio groups, invalid references and arithmetic
overflow. Shared writer checks verify measured metadata from real AAC and MP3
tracks after completion and disposal. Target-duration tests cover exact rounding,
packet-grid overshoot and measurement-window changes.

The public audio exporter independently checks complete playlist decoding,
every packet timestamp and every segment's coded extent. An exact rational
reference calculates peak and average rates from actual file sizes and coded
sample counts, including a real 12-second stereo AAC preparation with a common
target of six.

| Audio presentation | Peak transport bits/s | Average transport bits/s |
| --- | ---: | ---: |
| Copied 48 kHz mono AAC-LC | 164,500 | 158,625 |
| Copied 44.1 kHz stereo MPEG-1 Layer III | 210,071 | 208,892 |
| Copied 22.05 kHz mono MPEG-2 Layer III | 152,163 | 151,187 |
| Encoded 12-second stereo AAC-LC | 170,000 | 169,625 |

The current validation passes 3,196 managed tests, all 30 package checks, the strict
package-only Native AOT test executable and Native AOT CLI compilation.
Release compilation reports zero warnings and errors;
formatting and license-header checks pass.

An actual master playlist combining prepared audio with independently requested
video now passes [complete decoding and request-order checks](hls-prepared-presentation.md).
Chrome/hls.js completes playback and buffered seeks. Smooth rendering, the common
VOD presentation boundary around audio priming and padding, and application
integration remain open. The CLI defaults to muxed encoding and also supports
[`hls encode --separate-audio`](hls-encoding.md), with audio renditions shared by
variants that use equal audio settings. See
[continuous audio HLS](audio-hls.md) and the measured
[independent interval boundary](audiovisual-windows.md#independently-encoded-interval-boundaries).

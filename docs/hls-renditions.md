# HLS rendition encoding

`Mediana.Streaming.Hls.HlsRenditionEncoder` creates an H.264 rendition ladder from
a borrowed `VideoSession` or a sequence of owned decoded frames plus its stream
description. It resizes and encodes each profile, schedules audio packets, checks
video segment alignment and writes media and master playlists.

```csharp
HlsRenditionOptions options = new()
{
    VideoProfiles = [new(1280, new() { Quantizer = 26 }), new(854, new() { Quantizer = 28 })],
    TargetDuration = new(2, new(1, 1))
};
HlsEncodingResult result = await HlsRenditionEncoder.EncodeAsync(
    videoSession, new HlsDirectoryOutput(outputDirectory), options, allocator,
    audio: audioSource, progress: update => Report(update),
    cancellationToken: cancellationToken);
```

The output directory must exist. `HlsDirectoryOutput` creates rendition
subdirectories and refuses to overwrite files. `IHlsPackageOutput` supports other
destinations, including object storage and memory. Segment streams transfer to the
writers and are closed by the operation. Publication transactions belong to the
destination; an error can leave a partial package.

The video session, audio source, destination and allocator remain caller-owned.
The operation disposes consumed input frames, encoders, resizers and writers on
success, cancellation and failure. Dispose the borrowed sources after completion
to release their retained decode buffers. Progress counts input time once across
all variants and includes a completion update after playlist writing.

An optional `HlsAudioSource` supplies copied audio or AAC encoding through the
public audio preparation APIs. For muxed AAC, configure one source profile per
video variant. `SeparateAudio` writes one media playlist per audio profile;
`AudioProfileIndices` maps video variants to those profiles. Omitting the mapping
selects profile zero. Repeated AAC bitrates share an encoder in
`HlsAacAudioSource`. The source remains borrowed throughout package generation.

The current rendition encoder uses the managed H.264 encoder and requires an
identity video display transform. Output segment and playlist generation uses the
same public pipeline in the CLI and library callers.

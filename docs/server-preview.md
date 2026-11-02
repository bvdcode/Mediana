# Container preview and decoding ownership

The preview sample opens a container once. It searches attachments before picture
tags using the registered PNG/JPEG signatures, then decodes the first recognized
image. When no registered image is found, it starts video decoding from the same
reader, packet index, configuration and allocator. Source selection is application
policy; the container/image/decoder modules remain independent.

```sh
dotnet run --project samples/Mediana.Sample -c Release -- --preview input.mkv preview.png
```

The sample registers MP4/MOV, Matroska, MP3 and FLAC readers, PNG/JPEG image
decoders and the H.264 video decoder. A video stream is unnecessary when an image
is selected. Image
selection does not rank covers by dimensions or picture type, retrieve external
references, or retry after a recognized image fails decoding.

The audio preview sample searches picture tags before attachments. It writes a
150-pixel PNG cover when a registered PNG/JPEG image is present; otherwise it
transfers the same opened container to audio decoding and streams RMS/peak JSON:

```sh
dotnet run --project samples/Mediana.Sample -c Release -- --audio-preview input.mp3 cover.png waveform.json
```

Only the selected output is created, and existing output files are rejected.
The sample registers WAV, MP3, FLAC, MP4/MOV and Matroska readers and PCM, MP3,
FLAC and AAC decoders. Waveform output follows the
[audio waveform sample's processing and timeline policy](audio-waveform.md).
Recognized corrupt covers and cancellation propagate as failures. Display shaping
and drawing waveform bars remain application responsibilities.

## Passing an opened container to a decoder

```csharp
await using ContainerSession container = await engine.OpenContainerAsync(source, options);
using ContainerImage? image = await engine.ReadContainerImageAsync(container, searchOptions);
if (image is null)
{
    await using VideoSession video = engine.OpenVideo(container);
    using VideoFrame? frame = await video.ReadFrameAsync();
}
```

`OpenAudio(container, streamIndex)` provides the same transfer for audio. Omitting
the stream index selects the first stream of the required media kind in container
order, as with the byte-source opening methods. Decoder registrations come from
the engine on which the transfer is requested.

- Successful transfer gives the decode session exclusive ownership. The original
  container becomes inactive; disposing it does not close the transferred reader.
- Creation errors and cancellation leave the original container active. Any decoder
  successfully created before a later initialization failure is disposed.
- Transfer is allowed after inspecting header metadata, pictures and attachments.
  It is rejected after any packet read, including an EOF check with no read limit.
- Disposed, faulted or concurrently used containers cannot be transferred.
- Original packet/frame limits and the allocation budget still apply. Transfer
  performs no source I/O and creates no second container index or configuration.
- The source remains borrowed. Returned image/packet/frame leases survive session
  disposal until their owners release them. All operations remain single-consumer.

## Presentation policy

Video target selection uses the midpoint when declared duration exceeds one
second, otherwise time zero. Unknown duration also selects zero. Indexed sources
seek to a validated access point; sequential sources decode forward. Timestamp
selection returns the first picture at or after the target and does not substitute
an earlier frame at EOF.

The output fits within 150×150 pixels with known sample aspect ratio and an
orthogonal display transform. Color conversion, orientation and resizing sample
only the required video pixels; there is no full-size intermediate RGB video
frame. JPEG Exif orientation is already applied by its image decoder, so the image
path uses an identity transform.

`SdrPreviewColorPolicy.Resolve(format)` is an explicit interpretation of missing
primaries, transfer and matrix labels. It preserves declared fields and gives
priority to declared matrix/primaries over SD/HD geometry. RGB defaults to sRGB;
YUV uses the existing SD/HD profiles. Range and chroma location are preserved.
The CLI and sample use this shared policy and report assumptions. Strict processing
operations do not choose it automatically. Unsupported declarations, ICC/custom
color descriptions and HDR without tone mapping still fail explicitly.

## Verification

Contract tests cover video/audio ownership transfer, failure after decoder creation,
cancellation, invalid/ambiguous selection, original read limits, consumed/faulted/
busy/disposed sessions, borrowed source position and retained output storage.

Real MP4/MKV inputs retain their complete independently referenced video pixels,
restart after EOF, and perform zero additional source reads during transfer.
Complete MP4/MKV/MP3 audio samples, format and timestamps match the byte-source
opening path. Cover inspection is allowed before either transfer.

The composed sample checks all independent pixels of covers in MP3, FLAC, M4A and
MKV without registering a video decoder, every JPEG Exif orientation, misleading
attachment names/MIME types and recognized corrupt images. The HD video sample
continues to match its complete independent sRGB PNG reference. These same real
scenarios are linked into the package-only Native AOT consumer.

The audio preview sample verifies one container open, source ownership and buffer
release for MP3/FLAC/M4A/MKV covers and complete WAV/MP3/FLAC/MP4 waveforms.
Cover pixels match independent references; waveform JSON matches the direct
waveform operation byte for byte. Recognized-image failures and cancellation do
not read another image or start audio decoding. These checks also run in the
package-only Native AOT consumer.

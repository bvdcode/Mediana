# Container images

Container image selection searches the independent picture and attachment
capabilities. Applications choose source priority and register the image
implementations that count as eligible images.

The search recognizes prefixes, returns the first recognized candidate's
original bytes and preserves its source and zero-based declaration index. Names,
extensions and declared MIME types do not select the image. External picture
references are skipped without network access. Recognition does not validate
the complete image; a subsequent decoding error is returned to the caller.

```csharp
using Mediana;
using Mediana.Codecs.Jpeg;
using Mediana.Codecs.Png;
using Mediana.Containers.Flac;
using Mediana.Containers.Matroska;
using Mediana.Containers.Mp3;
using Mediana.Containers.Mp4;
using Mediana.IO;

MediaEngine engine = new MediaEngineBuilder()
    .AddContainer(new Mp3ReaderFactory())
    .AddContainer(new FlacReaderFactory())
    .AddContainer(new Mp4ReaderFactory())
    .AddContainer(new MatroskaReaderFactory())
    .AddImageDecoder(new PngImageDecoder())
    .AddImageDecoder(new JpegImageDecoder())
    .Build();
ContainerImageSearchOptions options = new(
    [ContainerImageSource.Attachment, ContainerImageSource.Picture]);
await using RandomAccessStreamByteSource source = new(File.OpenRead(input));
await using ContainerSession session = await engine.OpenContainerAsync(source);
using ContainerImage? image = await engine.ReadContainerImageAsync(session, options);
if (image is null)
{
    return;
}
await session.DisposeAsync();
await using FileStream destination = File.Create(output);
await destination.WriteAsync(image.Data);
```

The example requires the Mediana, MP3, FLAC, MP4, Matroska, PNG and JPEG packages.
`input` and `output` are application paths. The destination receives the original
encoding, so its extension is not a conversion request. Applications own their
overwrite and transactional-write policy.

`ContainerImage` owns a payload lease and must be disposed. Its borrowed `Data`
is usable after session/source disposal, until image disposal. The same memory
can be passed directly to `MediaEngine.DecodeImageAsync(ReadOnlyMemory<byte>, ...)`
without cloning the encoded input. The decoded frame owns independent storage.
JPEG decoding still requires the source interpretation described in the
[JPEG API](jpeg.md) when metadata does not establish it.

## Ordering, bounds and session behavior

- The required source list is copied and validated: at least one source, no
  repeated or unknown values. Entries retain their original container order.
  There is no implicit preference for a front cover, dimensions or file size.
- `MaxCandidates` bounds inspected declarations, including external references;
  its default is 256. Exceeding it throws rather than returning a false absence.
- `Image.MaxProbeBytes` bounds the registered recognition requirement.
  `Image.MaxInputBytes` bounds a recognized payload before it is fully read.
  The defaults are 64 KiB and 64 MiB respectively. A recognized oversized image
  fails explicitly instead of silently selecting a later image.
- Probe storage uses the container session's allocator and is released before
  acquiring the selected payload. Container metadata/index/read limits still
  apply. This bounds the additional search buffer, not total process memory.
- The session is borrowed under its existing single-consumer contract. Reads
  preserve packet order and random-access source position. Each read uses the
  same session concurrency, cancellation and failure rules as picture/attachment
  extraction; a failed reader operation faults the session.
- A missing capability is skipped. A null result means no registered image
  implementation recognized the exposed candidates that were inspected. It is
  not proof that the file contains no images. A search requires image registrations.

`IContainerPictureReader.ReadPicturePrefixAsync` and
`IContainerAttachmentReader.ReadAttachmentPrefixAsync` copy exactly
`min(destination.Length, declared byte length)` bytes, preserve the destination
tail and do not advance packets. Implementations must supply these operations;
the interfaces do not provide a complete-payload read as a substitute.
Matroska reads only the requested FileData range. MP3/FLAC/MP4 prefixes borrow
already retained picture metadata without allocating another complete payload.

## Command line

```sh
mediana cover select input.mkv selected-image.bin
mediana cover select input.m4a selected-image.bin --first Picture
mediana cover select input.flac selected-image.bin --overwrite
```

`cover select` inspects attachments first, then picture tags. `--first Picture`
reverses these sources. Its image registry includes PNG, JPEG, HEIF, JPEG 2000
and SVG. It extracts original bytes and reports the source, original index,
recognizing implementation and byte count. Existing overwrite confirmation,
`--overwrite`, `-y` and atomic output replacement apply.

`cover input output --index N` continues to extract a specific picture tag,
while `attachment input output --index N` extracts an arbitrary attached file.
Frame selection and choosing a cover versus an audio waveform remain explicit
application operations.

## Verification

Managed tests and a package-only Linux x64 Native AOT consumer check original
PNG covers from real MP3, FLAC, M4A and MKV files, plus second JPEG pictures in
FLAC/M4A. Returned storage survives session/source disposal and decodes through
the borrowed-memory API. Tests cover both source priorities, misleading names
and MIME types, external references, corrupt recognized images, exact allocator
budgets, declared limits, cancellation and concurrent operations.

A sparse declared 3 GiB attachment allows a 12-byte prefix read without loading
the remaining data or allocating payload storage. Actual managed/native CLI
checks extract ten original PNGs across five inputs, including an independently
authored MKV with a nonimage named `cover.png` before a PNG named `font.ttf`.
All extracted bytes and complete independently decoded RGBA pixels match.
Both overwrite options and preservation of approved outputs on failed selection
pass. Complete application and cross-platform acceptance remain separate.

# Media operations

The `Mediana` facade exposes reusable operations without console, logging or file-path
requirements. Applications register their modules with `MediaEngineBuilder` and supply
byte sources, destination streams, allocation budgets and cancellation tokens.

`OpenVisualAsync` recognizes an image, document page, animation or container video from
content. A `VisualSession` returns independently owned frames through `ReadFrameAsync`.
The session borrows the source and allocator. Random-access sources retain their sequential
cursor. Each frame must be disposed, including after session disposal. Session calls must
not overlap; failed reads require disposal. Image/probe storage uses the supplied allocator;
container video decoding uses the budget in `VisualOpenOptions.Media`.

`VisualOpenOptions.Request` selects the zero-based document page or elapsed media time.
`StreamIndex` selects container video and is rejected for images/documents. Video rejects
nonzero document page selection. A nonzero video time uses indexed seeking and then discards
required preroll up to the first presentation timestamp at or after the target.
`ReadImageSequence` selects animation enumeration instead of a single image.

`ExportPreviewAsync` applies display orientation, sample aspect ratio and sRGB conversion,
then calls the supplied `IImageEncoder`. `ExportAnimationAsync` does the same for an
`IImageSequenceEncoder`, including cumulative duration quantization. These paths use registered
interfaces and do not contain dispatch rules for individual media formats. Animated output
streams through the encoder. Encoded image/animation input remains retained during sequence
decoding; encoded container video is read through its container reader.

```csharp
MediaEngine engine = new MediaEngineBuilder()
    .AddContainer(new Mp4ReaderFactory(new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
    .AddVideoDecoder(new H264DecoderFactory())
    .AddImageDecoder(new PngImageDecoder())
    .AddImageDecoder(new PdfImageDecoder())
    .Build();

await using RandomAccessStreamByteSource source = new(File.OpenRead("input"));
await using FileStream output = File.Create("preview.png");
MediaBufferAllocator allocator = new(64 * 1024 * 1024);
VisualExportResult result = await engine.ExportPreviewAsync(source, output,
    new PngImageEncoder(), allocator, new()
    {
        MaximumWidth = 640,
        MaximumHeight = 480,
        WarningHandler = message => Console.Error.WriteLine(message)
    }, cancellationToken);
```

The example uses `Mediana`, `Mediana.Codecs.H264`, `Mediana.Codecs.Png`,
`Mediana.Containers.Mp4`, `Mediana.Documents.Pdf`, `Mediana.Core.Memory` and `Mediana.IO`.

Color conversion remains strict by default. Applications may explicitly choose
`VisualExportOptions.ColorResolver = SdrPreviewColorPolicy.Resolve` from
`Mediana.Processing` for the documented interpretation of missing SDR labels. Assumptions
are reported to `WarningHandler`; source metadata remains unchanged. HDR still requires
an explicit tone-mapping operation. The same warning callback receives image/document
recovery diagnostics. Animation export reports repeated warnings once per operation.
Unsupported features, corrupt data, cancellation, allocation failure and destination
write errors propagate. No operation retries a failed decoder as another format.

An explicitly selected format can set `ImageOpenOptions.DecoderImplementationId` to a
registered implementation. That decoder validates the input even when recognition would
fail; an invalid PDF therefore remains invalid PDF data. Embedded images keep the full
shared decoder registry. `PdfImageDecoder` additionally accepts `embeddedImageIndex` to
extract a decoded image from the selected page through the same image export path.

`TranscodeVideoAsync` uses registered decoder, video encoder and container writer factories.
It accepts optional output dimensions and requires explicit `OmitAudio` for an input that
contains audio. Display transforms are currently required to be identity. `TranscodeAudioAsync`
prepares mono/stereo interleaved Float32 for a registered audio encoder and muxer. Its result
includes priming/padding when the encoder implements `IAudioEncoderTiming`. `RemuxAsync`
copies selected compressed streams and lets the registered writer validate compatibility.

`ExportPcmAsync` accepts an `IAudioFrameWriterFactory`, such as `WavePcmWriterFactory` from
`Mediana.Containers.Wave`. It converts sample representation without changing sample rate
or channel layout. The facade depends on the frame-writer contract, and container modules
remain independent of processing implementations. `MonoAudioReader` in
`Mediana.Processing.Audio` shares explicit mono mixing, resampling and sample conversion
between waveform analysis and PCM export. `PcmStreamWriter.WriteFramesAsync` from
`Mediana.IO` writes a stable interleaved format with explicit byte order.

Operations borrow their destinations. Only successful completion finalizes container output;
partial output must be discarded on failure. File replacement, temporary files and overwrite
confirmation belong to the application. `MediaOutputProgress` reports processed duration,
written bytes and completion; declared total duration is optional. Written bytes count
successful writes, including header rewrites. Applications decide how to render progress,
estimates and diagnostics.

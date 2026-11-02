# Decoded video time windows

`Mediana.Processing.VideoTrimmer` selects whole decoded frames by exact rational
presentation time. It accepts an owned-frame callback and a half-open interval
`[start, end)`. A missing end reads through EOF; an equal start and end reads
nothing. EOF can end a window before its requested end.

```csharp
MediaTimestamp start = new(540_000, new(1, 1000));
MediaTimestamp end = new(546_000, new(1, 1000));
await session.SeekAsync(start, cancellationToken);
await using VideoTrimmer selected = new(session.ReadFrameAsync, start, end);
while (true)
{
    using VideoFrame? frame = await selected.ReadFrameAsync(cancellationToken);
    if (frame is null)
    {
        break;
    }
    await pipeline.WriteAsync(frame, cancellationToken);
}
```

The caller supplies the configured video session and encoding pipeline. Seeking
uses the session's existing container index and codec restart validation. The
trimmer itself only reads the callback, so it also composes with a sequential
source when that is the caller's explicit choice.

A selected frame is the original callback object: its pixels, presentation time,
format and duration are unchanged. There is no additional pixel allocation or
copy. Frames before the start and the first frame at or beyond the end are
disposed. The source remains borrowed; disposing the trimmer does not close it
or invalidate frames already returned to the caller.

Input PTS must be known and strictly increasing in presentation order. Missing,
duplicate or decreasing times fail explicitly. Cancellation and source failures
make the trimmer terminal. Concurrent reads and disposal are rejected. No frame
rate is inferred, and no frame is duplicated or substituted.

The end excludes frames starting at that time. A selected frame's duration can
extend past the end: selection does not shorten its presentation. Segment plans
must use actual frame boundaries and durations. This follows the timestamp-based
selection convention described by [FFmpeg's trim filter](https://ffmpeg.org/ffmpeg-filters.html#trim).

## Runnable interval transcode

```sh
dotnet run -c Release --project samples/Mediana.Sample -- \
  --transcode-window input.mp4 output.ts 540000 546000
```

The example explicitly seeks, selects decoded pictures, and uses the existing
H.264 encoder and MPEG-TS writer. It preserves source presentation times; the TS
writer applies its configured timestamp offset, one second by default. Each new
encoder starts with an independently decodable IDR. The example writes video
only, requires an identity display transform, and creates a new output file.
Failed or cancelled partial output must be discarded.

Thirty-two out-of-order requests over original CFR, VFR, edited and HD B-picture
inputs verify every selected picture against independent complete YUV hashes
and movie timestamps. Managed and Native AOT checks also verify object identity,
a one-frame pixel budget, cancellation, source failures and independent lifetimes.
Actual managed and native interval transcodes produce identical TS bytes on the
four inputs; all resulting pictures and timestamps are independently decoded.

The 540–546 second interval of the original 1280×720 film selects 144 pictures,
reading 145 decoded pictures and 12,037,169 source bytes, including the index,
from the 416,751,190-byte input. All 199,065,600 selected YUV bytes and presentation
times match independent decoding. Its complete 144-picture re-encoded transport
also independently decodes with the expected original clock plus the TS offset.
These are interval correctness and bounded-access checks, not an encoder speed
comparison or complete independently requested A/V HLS acceptance.

## Decode and presentation intervals

MP4 sample durations describe decode-order intervals. With reordered pictures,
these intervals can differ from the time a decoded picture is displayed. Passing
a sample's decode duration directly to the encoder caused full VFR transcodes to
fail at cadence changes: a 50 ms decode interval could overlap the next picture,
whose presentation time was only 20 ms later.

`IIndexedPresentationTimingReader` provides optional presentation intervals for
indexed packets containing complete presentation units. The selected decode
cursor uses this duration with the original encoded payload, PTS and DTS. Raw
sequential and indexed packets retain their original sample durations, preserving
copy and remux behavior. `MediaPacket.RetainWithDuration` creates an independent
lease on the same immutable buffer; it does not copy encoded bytes. A null
presentation interval remains unknown when passed to the decoder.

For reordered MP4 tracks, the reader lazily sorts sample timestamps from metadata.
Consecutive distinct presentation times define each interval. The final displayed
sample keeps its own decode duration, following [the composition offset model](https://developer.apple.com/documentation/quicktime-file-format/composition_offset_atom).
Tied composition times in this index have unknown intervals. Signed composition
offsets and linear movie time mapping preserve the result.

The index performs no encoded-media I/O and preserves packet cursors. Construction
uses O(n log n) time and 20 bytes per sample at peak, charged to the media buffer
budget. It retains eight bytes per sample only when intervals need correction;
unchanged tracks release the expanded storage. Tracks without composition offsets
or with a constant offset use their original durations directly. Cancellation or
a budget failure releases temporary storage and leaves construction retryable.

`MediaEngine` sessions apply this capability automatically. Applications connecting
an indexed reader directly to a codec can query it and retain the packet with that
duration explicitly; codecs do not infer container timing from compressed pictures.
The interval selector continues to preserve the duration supplied by its callback.

Complete CFR, VFR, edited VFR and HD transcodes now finish and independently decode
with every original presentation time. The CFR transport remains byte-for-byte
identical to the preceding output. Independent AAC segment boundaries remain open.

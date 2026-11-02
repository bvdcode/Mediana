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

Complete transcoding of the reordered VFR and edited VFR fixtures exposes an
existing timing issue: decoded frame durations still inherit decode-order sample
durations, which can overlap the next presentation time at cadence changes.
Both the preceding and current sample reject those full inputs. The interval
selector preserves that information rather than changing durations. Separating
these intervals is required by [the composition offset model](https://developer.apple.com/documentation/quicktime-file-format/composition_offset_atom).
Presentation duration handling and independent AAC segment boundaries remain open.

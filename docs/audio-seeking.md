# Indexed audio seeking

`AudioSession.CanSeek` reports whether the container and decoder provide a
supported indexed restart path. MP4/MOV AAC-LC sessions support this operation.
`SeekAsync(target)` positions the first returned PCM block at or before the
requested presentation time; an exact sample window then selects `[start, end)`.

```csharp
MediaEngine engine = new MediaEngineBuilder()
    .AddContainer(new Mp4ReaderFactory(new Mp4ReadOptions
    {
        TimestampMode = Mp4TimestampMode.LinearMovie
    }))
    .AddAudioDecoder(new AacDecoderFactory())
    .Build();
await using AudioSession session = await engine.OpenAudioAsync(source);
await session.SeekAsync(start);
await using AudioTrimmer window = AudioTrimmer.CreateTimeWindow(
    session.ReadFrameAsync, start, end, allocator);
using AudioFrame? first = await window.ReadFrameAsync();
```

Seeking can move backward or restart after EOF. The first packet retains ordinary
decoder startup priming when the target precedes the original stream beginning.
Returned frames keep independent storage ownership across seeks and disposal.
Packet and public frame-read budgets remain cumulative. Cancellation and restart
failure make the session terminal; overlapping calls and disposal are rejected.

## Recovery contracts

The execution pipeline composes two optional capabilities without codec or
container format branches:

- `IPacketPrerollDecoder` declares required history in encoded packets and resets
  while suppressing the output associated with the requested leading packets.
- `IIndexedPrerollReader.GetPrerollStart` combines that history with container
  recovery declarations without reading encoded media or moving reader cursors.

AAC-LC needs one preceding access unit to reconstruct overlap and window state.
The decoder processes that history without allocating a returned PCM frame.
MP4 combines this minimum with negative `roll` and `prol` distances. `prol` also
requires a sync sample at or before the backed-up position. History is clamped
at the original stream beginning. Forward recovery and unavailable required sync
points fail explicitly. See [MP4 recovery groups](mp4-recovery.md).

The existing `IRandomAccessInspector` contract remains available for independently
restartable packets, including H.264 access points. Codecs without either supported
restart contract do not gain seeking from indexed container access alone.

## Timelines and checks

Seeking preserves the reader's selected media or movie clock. Linear movie mode
translates timestamps through supported edits; it retains encoded packets before
and after the visible edit. Clamp sample selection to the explicit edit interval
when that interval is required. `Mp4AudioSampleRange.StartSample` counts from media
time zero and must not be applied again as a relative skip after seeking.
`GetPresentationEnd(decodedSampleRate)` provides the first excluded sample
instant on the movie grid. The audiovisual interval sample composes time windows
for this edit and the requested interval after indexed restart.

Exhaustive packet restarts in mono, stereo, 5.1 and window-transition inputs match
sequential PCM byte for byte in both timestamp modes. Sequential PCM is also
checked against independent Float32 references. Tests cover EOF restarts,
retained output, cumulative budgets, asynchronous cancellation and concurrent
operations. A late packet read accesses just its one preceding AAC packet and
the requested output packet, without decoding the earlier prefix.

Noise substitution can produce a different valid random phase after restarting.
Checks cover finite PCM, independently decoded channel-block energy, and declared
spectral energy and stereo correlation across different generator states. They
do not require pointwise identity for independently generated noise.

Independent output AAC encoder priming, padding and gapless joins remain separate
from input decoder preroll; see [audio/video intervals](audiovisual-windows.md).

## Complete late-interval comparison

A Linux x64 Native AOT consumer selects the complete 530–590 second audio
interval from a 596.46-second recording: 48 kHz, six channels, 2,880,000 samples
per channel and 2,814 returned blocks. The same binary compares sequential
prefix decoding with indexed restart. Both paths hash every selected PCM byte
and timestamp. Eight serial runs alternate order on the same pinned logical
processor with `DOTNET_PROCESSOR_COUNT=1`; values below are four-run medians.

| Measurement | Sequential selection | Indexed selection |
| --- | ---: | ---: |
| Process CPU | 2.923 s | 0.500 s |
| Elapsed time | 2.924 s | 0.500 s |
| Peak process RSS | 16.75 MiB | 16.56 MiB |
| Cumulative managed allocation | 753.79 MiB | 78.34 MiB |
| Input bytes read | 31.16 MiB | 3.54 MiB |

Indexed selection uses 5.85 times less CPU for this request and reduces
cumulative allocation by 89.6%. Peak RSS is effectively unchanged. The gain
comes from avoiding the unwanted prefix; it does not measure faster decoding
per access unit or full-file H.264/HLS encoding.

All selected PCM bytes and timestamps are identical between both paths.
FAAD2 2.11.4 independently decodes every selected sample within 2.09 × 10⁻⁷
Float32 units. The transport used for that reference preserves SHA-256 of all
27,960 original encoded audio packets. Every run releases all media buffers.

A separate two-run FFmpeg 6.1.1 control uses indexed input seeking, one decoder
thread, Float32 output and SHA-256: median CPU 0.336 s, elapsed 0.339 s and peak
RSS 57.43 MiB. Mediana therefore still uses 1.49 times as much CPU in this
specific comparison. FFmpeg differs in 24 front-right blocks around additional
window transitions permitted by ISO/IEC 14496-3 §4.6.11.3.1; its overlap handling
differs from the literal windowed overlap-add in §4.6.11.3.3 and from FAAD2.
The other five channels agree within 1.79 × 10⁻⁷. The same difference is present
with sequential selection; the comparison does not claim identical FFmpeg PCM
or a general codec speed ranking. The complete H.264/HLS CPU gap remains open.

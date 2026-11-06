// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Codecs.Gif
{
    /// <summary>Decodes GIF87a/89a palettes, interlacing and animated disposal to straight RGBA8.
    /// Sequences enumerate one cycle with original centisecond delays, including zero delays.
    /// Canvas, disposal backups and yielded frame storage share the caller's live-buffer budget.</summary>
    public class GifImageDecoder : ISynchronousImageDecoder, IImageSequenceDecoder, IImageInfoReader
    {
        public string ImplementationId => "gif.managed.image";
        public int ProbeLength => 6;
        public ImageDecoderFeatures Features => ImageDecoderFeatures.Animation;
        public bool CanDecode(ReadOnlySpan<byte> prefix, long? encodedLength = null) => prefix.StartsWith("GIF87a"u8) || prefix.StartsWith("GIF89a"u8);
        public ValueTask<ImageInfo> ReadInfoAsync(ReadOnlyMemory<byte> encoded, CancellationToken cancellationToken = default)
        {
            GifDocument document = new(encoded, cancellationToken);
            return ValueTask.FromResult(new ImageInfo("gif", document.Width, document.Height, ImageDimensionUnit.Pixels,
                duration: new(document.Duration, new(1, 100)), metadata: new("image",
                    [.. document.Properties, new("gif", "frame_count", document.Frames.Count), new("gif", "background_index", document.BackgroundIndex)],
                    document.Frames.Select((frame, index) => new MediaMetadata("frame/" + index,
                        [new("gif", "rectangle", frame.Left, frame.Top, frame.Width, frame.Height), new("gif", "delay_centiseconds", frame.Delay),
                            new("gif", "disposal", frame.Disposal), new("gif", "transparent_index", frame.TransparentIndex), new("gif", "interlaced", frame.Interlaced)])))));
        }
        public ValueTask<VideoFrame> DecodeAsync(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default) => ValueTask.FromResult(Decode(encoded, request, allocator, cancellationToken));
        public VideoFrame Decode(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(allocator);
            request.Validate(Features); cancellationToken.ThrowIfCancellationRequested();
            GifDocument document = new(encoded, cancellationToken);
            long time = request.Time?.Rescale(new(1, 100), TimestampRounding.Floor).Value ?? 0;
            if (time >= document.Duration && time != 0) { throw new ArgumentOutOfRangeException(nameof(request), "The selected time is outside the GIF cycle."); }
            using GifCanvas canvas = new(document, allocator, request, cancellationToken);
            long start = 0;
            for (int index = 0; index < document.Frames.Count; index++)
            {
                GifFrame frame = document.Frames[index];
                canvas.Draw(frame, cancellationToken);
                if (time == 0 || time < start + frame.Delay) { return canvas.Frame(start, frame.Delay, true); }
                start += frame.Delay;
            }
            throw new InvalidDataException("GIF has no frame at the selected time.");
        }
        public async IAsyncEnumerable<VideoFrame> DecodeFramesAsync(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(allocator);
            request.Validate(Features); cancellationToken.ThrowIfCancellationRequested();
            GifDocument document = new(encoded, cancellationToken);
            long time = request.Time?.Rescale(new(1, 100), TimestampRounding.Floor).Value ?? 0;
            if (time >= document.Duration && time != 0) { throw new ArgumentOutOfRangeException(nameof(request), "The selected time is outside the GIF cycle."); }
            using GifCanvas canvas = new(document, allocator, request, cancellationToken);
            long start = 0;
            foreach (GifFrame frame in document.Frames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                canvas.Draw(frame, cancellationToken);
                if (time == 0 || time < start + frame.Delay)
                {
                    await Task.Yield();
                    yield return canvas.Frame(start, frame.Delay, false);
                }
                start += frame.Delay;
            }
        }
    }
}

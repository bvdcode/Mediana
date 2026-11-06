// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Codecs.Webp
{
    /// <summary>Decodes WebP images and one animation cycle to independently owned straight RGBA8 frames.
    /// Encoded data is borrowed. Decoded pixels and entropy workspace share the caller's allocator.</summary>
    public class WebpImageDecoder : ISynchronousImageDecoder, IImageSequenceDecoder, IImageInfoReader
    {
        public string ImplementationId => "webp.managed.image";
        public int ProbeLength => 12;
        public ImageDecoderFeatures Features => ImageDecoderFeatures.Animation;
        public bool CanDecode(ReadOnlySpan<byte> prefix, long? encodedLength = null) => prefix.Length >= 12
            && prefix.StartsWith("RIFF"u8) && prefix.Slice(8, 4).SequenceEqual("WEBP"u8);
        public ValueTask<ImageInfo> ReadInfoAsync(ReadOnlyMemory<byte> encoded, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new WebpDocument(encoded, cancellationToken).Inspect(cancellationToken));
        public ValueTask<VideoFrame> DecodeAsync(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(Decode(encoded, request, allocator, cancellationToken));
        public VideoFrame Decode(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request, MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(allocator);
            request.Validate(Features); cancellationToken.ThrowIfCancellationRequested();
            WebpDocument document = new(encoded, cancellationToken);
            long time = SelectedTime(document, request);
            using WebpCanvas canvas = new(document, request, allocator, cancellationToken);
            long start = 0;
            foreach (WebpDecodedFrame frame in document.Frames)
            {
                canvas.Draw(frame, cancellationToken);
                if (time == 0 || time < start + frame.Duration) { return canvas.Frame(start, frame.Duration, true); }
                start += frame.Duration;
            }
            throw new InvalidDataException("WebP has no frame at the selected time.");
        }
        public async IAsyncEnumerable<VideoFrame> DecodeFramesAsync(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(allocator);
            request.Validate(Features); cancellationToken.ThrowIfCancellationRequested();
            WebpDocument document = new(encoded, cancellationToken);
            long time = SelectedTime(document, request);
            using WebpCanvas canvas = new(document, request, allocator, cancellationToken);
            long start = 0;
            foreach (WebpDecodedFrame frame in document.Frames)
            {
                canvas.Draw(frame, cancellationToken);
                if (time == 0 || time < start + frame.Duration)
                {
                    await Task.Yield(); yield return canvas.Frame(start, frame.Duration, false);
                }
                start += frame.Duration;
            }
        }
        private static long SelectedTime(WebpDocument document, ImageDecodeRequest request)
        {
            long time = request.Time?.Rescale(new(1, 1000), TimestampRounding.Floor).Value ?? 0;
            if (time >= document.Duration && time != 0) { throw new ArgumentOutOfRangeException(nameof(request), "The selected time is outside the WebP animation cycle."); }
            return time;
        }
    }
}

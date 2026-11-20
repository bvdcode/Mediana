// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Codecs.Bmp
{
    /// <summary>Decodes RIFF ACON animations whose frames contain ICO/CUR images. PageIndex selects a
    /// presentation step; Time selects elapsed time in one cycle. The first icon entry supplies each step's pixels.</summary>
    public class AniImageDecoder : ISynchronousImageDecoder, IImageSequenceDecoder, IImageInfoReader
    {
        public string ImplementationId => "ani.managed.image";
        public int ProbeLength => 12;
        public ImageDecoderFeatures Features => ImageDecoderFeatures.Pages | ImageDecoderFeatures.Animation;
        public bool CanDecode(ReadOnlySpan<byte> prefix, long? encodedLength = null) => prefix.Length >= 12 &&
            prefix.StartsWith("RIFF"u8) && prefix.Slice(8, 4).SequenceEqual("ACON"u8);

        public async ValueTask<ImageInfo> ReadInfoAsync(ReadOnlyMemory<byte> encoded, CancellationToken cancellationToken = default)
        {
            AniDocument document = new(encoded, cancellationToken);
            IcoImageDecoder reader = new();
            List<MediaMetadata> frames = [];
            ImageInfo? first = null;
            for (int index = 0; index < document.Frames.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ImageInfo image = await reader.ReadInfoAsync(document.Frames[index], cancellationToken).ConfigureAwait(false);
                if (index == document.FrameIndex(0)) { first = image; }
                frames.Add(new("frame/" + index, [new("ani", "dimensions", image.Width, image.Height)], [image.Metadata]));
            }
            List<MediaMetadata> steps = [];
            for (int step = 0; step < document.StepCount; step++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                steps.Add(new("step/" + step, [new("ani", "frame", document.FrameIndex(step)), new("ani", "duration_jiffies", document.Rate(step))]));
            }
            return new("ani", first!.Width, first.Height, ImageDimensionUnit.Pixels, document.StepCount,
                duration: new(document.Duration, AniDocument.Clock), metadata: new("image",
                    [new("ani", "frame_count", document.Frames.Count), new("ani", "step_count", document.StepCount),
                        new("ani", "declared_dimensions", document.DeclaredWidth, document.DeclaredHeight)], [.. frames, .. steps]));
        }

        public async ValueTask<VideoFrame> DecodeAsync(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            Validate(request, allocator, cancellationToken);
            if (request.Images is null) { return await BitmapImageDecoders.Default.DecodeAsync(encoded, request, allocator, cancellationToken).ConfigureAwait(false); }
            AniDocument document = new(encoded, cancellationToken);
            (int step, long start) = document.Select(request, cancellationToken);
            return await DecodeStepAsync(document, step, start, request, allocator, cancellationToken).ConfigureAwait(false);
        }

        public VideoFrame Decode(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            Validate(request, allocator, cancellationToken);
            if (request.Images is null) { return BitmapImageDecoders.Default.Decode(encoded, request, allocator, cancellationToken); }
            AniDocument document = new(encoded, cancellationToken);
            (int step, long start) = document.Select(request, cancellationToken);
            using VideoFrame frame = request.Images.Decode(document.Frames[document.FrameIndex(step)], Nested(request), allocator, cancellationToken);
            return frame.WithTiming(new(start, AniDocument.Clock), new(document.Rate(step), AniDocument.Clock));
        }

        public async IAsyncEnumerable<VideoFrame> DecodeFramesAsync(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Validate(request, allocator, cancellationToken);
            if (request.Images is null)
            {
                await foreach (VideoFrame frame in BitmapImageDecoders.Default.DecodeFramesAsync(encoded, request, allocator, cancellationToken).ConfigureAwait(false)) { yield return frame; }
                yield break;
            }
            AniDocument document = new(encoded, cancellationToken);
            (int first, long start) = document.Select(request, cancellationToken);
            for (int step = first; step < document.StepCount; step++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return await DecodeStepAsync(document, step, start, request, allocator, cancellationToken).ConfigureAwait(false);
                start += document.Rate(step);
            }
        }

        private static async ValueTask<VideoFrame> DecodeStepAsync(AniDocument document, int step, long start, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken token)
        {
            using VideoFrame frame = await request.Images!.DecodeAsync(document.Frames[document.FrameIndex(step)], Nested(request), allocator, token).ConfigureAwait(false);
            return frame.WithTiming(new(start, AniDocument.Clock), new(document.Rate(step), AniDocument.Clock));
        }
        private static ImageDecodeRequest Nested(ImageDecodeRequest request) => request with { PageIndex = 0, Time = null };
        private void Validate(ImageDecodeRequest request, MediaBufferAllocator allocator, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(allocator);
            request.Validate(Features); token.ThrowIfCancellationRequested();
        }
    }
}

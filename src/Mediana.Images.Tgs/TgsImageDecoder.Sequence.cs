// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using Mediana.Codecs;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Images.Tgs
{
    public partial class TgsImageDecoder : IImageSequenceDecoder
    {
        public async IAsyncEnumerable<VideoFrame> DecodeFramesAsync(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(allocator);
            request.Validate(Features);
            TgsDocument document = TgsReader.Read(encoded, readerOptions, cancellationToken);
            MediaTimestamp start = request.Time ?? new(0, TimeBase.TimeSpanTicks);
            double startSeconds = start.Value * (double)start.TimeBase.Numerator / start.TimeBase.Denominator;
            double first = Math.Ceiling(startSeconds * document.FrameRate);
            double count = Math.Ceiling(document.OutPoint - document.InPoint);
            if (!double.IsFinite(count) || count > long.MaxValue || first > long.MaxValue)
            {
                throw new NotSupportedException("Animation frame count exceeds the supported range.");
            }
            for (long index = checked((long)first); index < (long)count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long begin = TimeSpan.FromSeconds(index / document.FrameRate).Ticks;
                long end = TimeSpan.FromSeconds(Math.Min((index + 1) / document.FrameRate, document.Duration)).Ticks;
                await Task.Yield();
                yield return Render(document, new(begin, TimeBase.TimeSpanTicks), new(end - begin, TimeBase.TimeSpanTicks), request, allocator, cancellationToken);
            }
        }
    }
}

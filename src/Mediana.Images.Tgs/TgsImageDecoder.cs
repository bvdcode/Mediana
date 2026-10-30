// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Images.Svg;

namespace Mediana.Images.Tgs
{
    /// <summary>Registered TGS/Lottie rendering at an elapsed time from the composition in-point.
    /// Output timestamps preserve the requested time; durations use the renderer's TimeSpan resolution.</summary>
    public class TgsImageDecoder(TgsReaderOptions? readerOptions = null, SvgRenderOptions? renderOptions = null) : IImageDecoder
    {
        public string ImplementationId => "tgs.managed.image";
        public int ProbeLength => 512;
        public ImageDecoderFeatures Features => ImageDecoderFeatures.Animation;
        public bool CanDecode(ReadOnlySpan<byte> prefix) => prefix.StartsWith<byte>([0x1f, 0x8b])
            || prefix.TrimStart(" \r\n\t"u8).StartsWith("{"u8);

        public ValueTask<VideoFrame> DecodeAsync(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(allocator);
            request.Validate(Features);
            TgsDocument document = TgsReader.Read(encoded, readerOptions, cancellationToken);
            MediaTimestamp time = request.Time ?? new MediaTimestamp(0, TimeBase.TimeSpanTicks);
            double seconds = time.Value * (double)time.TimeBase.Numerator / time.TimeBase.Denominator;
            TgsFrame rendered = TgsRenderer.RenderFrameDetailed(document, document.InPoint + seconds * document.FrameRate,
                renderOptions, cancellationToken);
            MediaDuration duration = new(rendered.Duration.Ticks, TimeBase.TimeSpanTicks);
            return ValueTask.FromResult(rendered.Image.ToVideoFrame(allocator, cancellationToken, time, duration));
        }
    }
}

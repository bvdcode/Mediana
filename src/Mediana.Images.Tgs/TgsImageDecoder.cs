// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using Mediana.Codecs;
using Mediana.Core.Metadata;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Images.Tgs
{
    /// <summary>Registered TGS/Lottie rendering at an elapsed time from the composition in-point.
    /// Output timestamps preserve the requested time; durations use the renderer's TimeSpan resolution.</summary>
    public partial class TgsImageDecoder(TgsReaderOptions? readerOptions = null, DrawingRenderOptions? renderOptions = null) : IImageDecoder, IImageInfoReader
    {
        public string ImplementationId => "tgs.managed.image";
        public int ProbeLength => 512;
        public ImageDecoderFeatures Features => ImageDecoderFeatures.Animation;
        public bool CanDecode(ReadOnlySpan<byte> prefix, long? encodedLength = null) => prefix.StartsWith<byte>([0x1f, 0x8b])
            || prefix.TrimStart(" \r\n\t"u8).StartsWith("{"u8);

        public ValueTask<ImageInfo> ReadInfoAsync(ReadOnlyMemory<byte> encoded, CancellationToken cancellationToken = default)
        {
            TgsDocument document = TgsReader.Read(encoded, readerOptions, cancellationToken);
            if (!double.IsFinite(document.Duration) || document.Duration >= TimeSpan.MaxValue.TotalSeconds)
            {
                throw new NotSupportedException("Animation duration exceeds the supported time range.");
            }
            MediaDuration duration = new(TimeSpan.FromSeconds(document.Duration).Ticks, TimeBase.TimeSpanTicks);
            string format = encoded.Span.StartsWith<byte>([0x1f, 0x8b]) ? "tgs" : "lottie";
            List<MediaProperty> properties = [new("lottie", "in_point", document.InPoint), new("lottie", "out_point", document.OutPoint),
                new("lottie", "frame_rate", document.FrameRate)];
            foreach (System.Text.Json.JsonProperty property in document.Root.EnumerateObject().Where(property => property.Name is "v" or "nm"))
            {
                if (property.Value.ValueKind != System.Text.Json.JsonValueKind.String)
                {
                    throw new InvalidDataException("Lottie " + property.Name + " must be a string.");
                }
                properties.Add(new("lottie", property.Name, property.Value.GetString()!));
            }
            return ValueTask.FromResult(new ImageInfo(format, document.Width, document.Height, ImageDimensionUnit.Pixels,
                duration: duration, frameRate: document.FrameRate,
                metadata: new("animation", properties)));
        }

        public ValueTask<VideoFrame> DecodeAsync(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(allocator);
            request.Validate(Features);
            TgsDocument document = TgsReader.Read(encoded, readerOptions, cancellationToken);
            MediaTimestamp time = request.Time ?? new MediaTimestamp(0, TimeBase.TimeSpanTicks);
            return ValueTask.FromResult(Render(document, time, null, request, allocator, cancellationToken));
        }

        private VideoFrame Render(TgsDocument document, MediaTimestamp time, MediaDuration? duration, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken)
        {
            double seconds = time.Value * (double)time.TimeBase.Numerator / time.TimeBase.Denominator;
            TgsFrame rendered = TgsRenderer.RenderFrameDetailed(document, document.InPoint + seconds * document.FrameRate,
                renderOptions, cancellationToken, allocator);
            using PixelBuffer pixels = rendered.Image;
            foreach (string warning in rendered.Warnings)
            {
                request.WarningHandler?.Invoke(warning);
            }
            return pixels.PublishVideoFrame(cancellationToken, time, duration ?? new(rendered.Duration.Ticks, TimeBase.TimeSpanTicks));
        }
    }
}

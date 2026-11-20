// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Processing
{
    public static partial class SrgbConverter
    {
        private static VideoFrame ApplyFloatingRgba<TTransform>(VideoFrame input, NearestNeighborMap map, SampleAspectRatio? aspectRatio,
            MediaBufferAllocator allocator, TTransform transform, CancellationToken token) where TTransform : struct, IRgbPixelTransform
        {
            VideoFormat format = new(map.OutputWidth, map.OutputHeight, VideoPixelFormat.Rgba32, SrgbColor, aspectRatio, null,
                VideoScanGeometry.Map(input.Format.ScanMode, map));
            PlaneLayout[] planes = format.CreateTightLayout();
            using WritableMediaBuffer writable = allocator.Allocate(checked((int)planes[0].EndOffset));
            int cachedY = -1;
            ReadOnlySpan<float> source = default;
            for (int y = 0; y < map.OutputHeight; y++)
            {
                token.ThrowIfCancellationRequested();
                Span<byte> row = writable.Span.Slice(y * map.OutputWidth * 4, map.OutputWidth * 4);
                for (int x = 0; x < map.OutputWidth; x++)
                {
                    if ((x & 1023) == 0) { token.ThrowIfCancellationRequested(); }
                    int sourceY = map.SourceY(x, y);
                    if (sourceY != cachedY) { source = MemoryMarshal.Cast<byte, float>(input.GetRow(0, sourceY)); cachedY = sourceY; }
                    ReadOnlySpan<float> pixel = source.Slice(map.SourceX(x, y) * 4, 4);
                    double alpha = pixel[3];
                    if (!float.IsFinite(pixel[0]) || !float.IsFinite(pixel[1]) || !float.IsFinite(pixel[2]) || !double.IsFinite(alpha))
                    { throw new InvalidDataException("Non-finite floating-point samples cannot be converted to an sRGB display image."); }
                    double inverse = alpha > 0 ? 1 / alpha : 0;
                    byte opacity = (byte)Math.Clamp((int)Math.Round(Math.Clamp(alpha, 0, 1) * 255, MidpointRounding.AwayFromZero), 0, 255);
                    transform.Write(row.Slice(x * 4, 4), pixel[0] * inverse, pixel[1] * inverse, pixel[2] * inverse, opacity);
                }
            }
            token.ThrowIfCancellationRequested();
            using MediaBuffer buffer = writable.Publish();
            return new(format, buffer, planes, input.Timestamp, input.Duration);
        }
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Core.Video
{
    /// <summary>Storage dimensions and color metadata. Display transforms are not applied implicitly.</summary>
    public class VideoFormat
    {
        public VideoFormat(int width, int height, VideoPixelFormat pixelFormat, VideoColorInfo? color = null,
            SampleAspectRatio? sampleAspectRatio = null, VideoColorDescription? colorDescription = null)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            ArgumentNullException.ThrowIfNull(pixelFormat);
            sampleAspectRatio?.Validate();
            Width = width;
            Height = height;
            PixelFormat = pixelFormat;
            Color = color ?? new VideoColorInfo();
            SampleAspectRatio = sampleAspectRatio;
            ColorDescription = colorDescription;
        }

        public int Width { get; }
        public int Height { get; }
        public VideoPixelFormat PixelFormat { get; }
        public VideoColorInfo Color { get; }
        public SampleAspectRatio? SampleAspectRatio { get; }
        /// <summary>Optional source description. Color retains the codec's labels.</summary>
        public VideoColorDescription? ColorDescription { get; }

        /// <summary>Applies the source description's precedence without modifying the stored metadata or sample values.</summary>
        public VideoColorInfo GetColorInterpretation() => ColorDescription?.Resolve(Color) ?? Color;

        public PlaneLayout[] CreateTightLayout()
        {
            PlaneLayout[] result = new PlaneLayout[PixelFormat.Planes.Count];
            int offset = 0;
            for (int i = 0; i < result.Length; i++)
            {
                VideoPlaneFormat plane = PixelFormat.Planes[i];
                int width = checked((int)(((long)Width + plane.HorizontalSubsampling - 1) / plane.HorizontalSubsampling));
                int height = checked((int)(((long)Height + plane.VerticalSubsampling - 1) / plane.VerticalSubsampling));
                int stride = checked(width * plane.BytesPerElement);
                result[i] = new PlaneLayout(offset, stride, stride, height);
                offset = checked(offset + stride * height);
            }
            return result;
        }
    }
}

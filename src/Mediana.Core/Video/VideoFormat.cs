// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Core.Video
{
    /// <summary>Storage dimensions and color metadata. Display transforms are not applied implicitly.</summary>
    public class VideoFormat
    {
        public VideoFormat(int width, int height, VideoPixelFormat pixelFormat, VideoColorInfo? color = null,
            SampleAspectRatio? sampleAspectRatio = null, VideoColorDescription? colorDescription = null)
            : this(width, height, pixelFormat, color, sampleAspectRatio, colorDescription, VideoScanMode.Progressive)
        {
        }

        public VideoFormat(int width, int height, VideoPixelFormat pixelFormat, VideoColorInfo? color,
            SampleAspectRatio? sampleAspectRatio, VideoColorDescription? colorDescription, VideoScanMode scanMode)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            ArgumentNullException.ThrowIfNull(pixelFormat);
            if (!Enum.IsDefined(scanMode))
            {
                throw new ArgumentOutOfRangeException(nameof(scanMode));
            }
            sampleAspectRatio?.Validate();
            Width = width;
            Height = height;
            PixelFormat = pixelFormat;
            ScanMode = scanMode;
            Color = color ?? new VideoColorInfo();
            SampleAspectRatio = sampleAspectRatio;
            ColorDescription = colorDescription;
        }

        public int Width { get; }
        public int Height { get; }
        public VideoPixelFormat PixelFormat { get; }
        public VideoScanMode ScanMode { get; }
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
                int height = PlaneHeight(plane.VerticalSubsampling);
                int stride = checked(width * plane.BytesPerElement);
                result[i] = new PlaneLayout(offset, stride, stride, height);
                offset = checked(offset + stride * height);
            }
            return result;
        }

        private int PlaneHeight(int verticalSubsampling) => ScanMode switch
        {
            VideoScanMode.Progressive or VideoScanMode.TopField or VideoScanMode.BottomField =>
                checked((int)(((long)Height + verticalSubsampling - 1) / verticalSubsampling)),
            VideoScanMode.TopFieldFirst or VideoScanMode.BottomFieldFirst =>
                checked((int)((((long)Height + 1) / 2 + verticalSubsampling - 1) / verticalSubsampling +
                    ((long)Height / 2 + verticalSubsampling - 1) / verticalSubsampling)),
            _ => throw new InvalidOperationException("The video scan mode is invalid.")
        };
    }
}

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
            : this(width, height, pixelFormat, color, sampleAspectRatio, colorDescription, scanMode, null)
        {
        }

        public VideoFormat(int width, int height, VideoPixelFormat pixelFormat, VideoColorInfo? color,
            SampleAspectRatio? sampleAspectRatio, VideoColorDescription? colorDescription, VideoScanMode scanMode, int? fullFrameHeight)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            ArgumentNullException.ThrowIfNull(pixelFormat);
            if (!Enum.IsDefined(scanMode))
            {
                throw new ArgumentOutOfRangeException(nameof(scanMode));
            }
            sampleAspectRatio?.Validate();
            if (fullFrameHeight is int declaredHeight)
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(declaredHeight);
            }
            FullFrameHeight = scanMode switch
            {
                VideoScanMode.Progressive or VideoScanMode.TopFieldFirst or VideoScanMode.BottomFieldFirst
                    when fullFrameHeight is null || fullFrameHeight == height => height,
                VideoScanMode.TopField when fullFrameHeight is null || ((long)fullFrameHeight.Value + 1) / 2 == height => fullFrameHeight,
                VideoScanMode.BottomField when fullFrameHeight is null || fullFrameHeight.Value / 2 == height => fullFrameHeight,
                _ => throw new ArgumentException("The full frame height does not match the stored rows and scan mode.", nameof(fullFrameHeight))
            };
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
        /// <summary>Full visible frame height. A single-field surface may leave it unknown; stored Height remains the number of field rows.</summary>
        public int? FullFrameHeight { get; }
        public VideoPixelFormat PixelFormat { get; }
        public VideoScanMode ScanMode { get; }
        public VideoColorInfo Color { get; }
        public SampleAspectRatio? SampleAspectRatio { get; }
        /// <summary>Optional source description. Color retains the codec's labels.</summary>
        public VideoColorDescription? ColorDescription { get; }

        /// <summary>Applies the source description's precedence without modifying the stored metadata or sample values.</summary>
        public VideoColorInfo GetColorInterpretation() => ColorDescription?.Resolve(Color) ?? Color;

        /// <summary>Describes a field view without creating pixel storage. Progressive chroma cannot be reinterpreted as independent fields.</summary>
        public VideoFormat GetFieldFormat(VideoFieldParity parity)
        {
            int offset = parity switch
            {
                VideoFieldParity.Top => 0,
                VideoFieldParity.Bottom => 1,
                _ => throw new ArgumentOutOfRangeException(nameof(parity))
            };
            switch (ScanMode)
            {
                case VideoScanMode.TopField when parity == VideoFieldParity.Top:
                case VideoScanMode.BottomField when parity == VideoFieldParity.Bottom:
                    return this;
                case VideoScanMode.TopField:
                case VideoScanMode.BottomField:
                    throw new ArgumentException("The requested field is not present in this surface.", nameof(parity));
                case VideoScanMode.Progressive:
                    throw new InvalidOperationException("Progressive chroma cannot be interpreted as independent fields without resampling.");
                case VideoScanMode.TopFieldFirst:
                case VideoScanMode.BottomFieldFirst:
                    break;
                default:
                    throw new InvalidOperationException("The video scan mode is invalid.");
            }
            int height = checked((int)(((long)Height + 1 - offset) / 2));
            if (height == 0)
            {
                throw new ArgumentException("The requested field has no visible rows.", nameof(parity));
            }
            VideoScanMode mode = parity switch
            {
                VideoFieldParity.Top => VideoScanMode.TopField,
                VideoFieldParity.Bottom => VideoScanMode.BottomField,
                _ => throw new ArgumentOutOfRangeException(nameof(parity))
            };
            return new(Width, height, PixelFormat, Color, SampleAspectRatio, ColorDescription, mode, Height);
        }

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

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Core.Metadata
{
    /// <summary>Codec-declared image properties, independent of decoded plane storage and container display transforms.</summary>
    public class VideoStreamConfiguration
    {
        public VideoStreamConfiguration(int width, int height, VideoChromaSampling chromaSampling,
            int lumaBitDepth, int? chromaBitDepth, bool? isProgressive = null,
            SampleAspectRatio? sampleAspectRatio = null, VideoColorInfo? color = null)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lumaBitDepth);
            if (!Enum.IsDefined(chromaSampling))
            {
                throw new ArgumentOutOfRangeException(nameof(chromaSampling));
            }
            if (chromaSampling == VideoChromaSampling.Monochrome)
            {
                if (chromaBitDepth is not null)
                {
                    throw new ArgumentException("Monochrome video has no chroma sample precision.", nameof(chromaBitDepth));
                }
            }
            else if (chromaBitDepth is null or <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chromaBitDepth));
            }
            sampleAspectRatio?.Validate();
            Width = width;
            Height = height;
            ChromaSampling = chromaSampling;
            LumaBitDepth = lumaBitDepth;
            ChromaBitDepth = chromaBitDepth;
            IsProgressive = isProgressive;
            SampleAspectRatio = sampleAspectRatio;
            Color = color ?? new VideoColorInfo();
        }

        /// <summary>Visible sample dimensions after codec cropping; pixel aspect and container rotation are not applied.</summary>
        public int Width { get; }
        public int Height { get; }
        public VideoChromaSampling ChromaSampling { get; }
        public int LumaBitDepth { get; }
        public int? ChromaBitDepth { get; }
        /// <summary>True for established progressive coding, false for established interlacing; null means unknown.</summary>
        public bool? IsProgressive { get; }
        public SampleAspectRatio? SampleAspectRatio { get; }
        public VideoColorInfo Color { get; }
    }
}

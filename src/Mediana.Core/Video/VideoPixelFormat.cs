// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Core.Video
{
    public class VideoPixelFormat
    {
        public VideoPixelFormat(string name, params VideoPlaneFormat[] planes)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(planes);
            if (planes.Length == 0 || planes.Any(plane => plane is null))
            {
                throw new ArgumentException("At least one non-null plane is required.", nameof(planes));
            }
            Name = name;
            Planes = Array.AsReadOnly((VideoPlaneFormat[])planes.Clone());
        }

        public string Name { get; }
        public IReadOnlyList<VideoPlaneFormat> Planes { get; }

        /// <summary>Red, green, blue and straight (unassociated) alpha bytes.</summary>
        public static VideoPixelFormat Rgba32 { get; } = new("rgba32", new VideoPlaneFormat(1, 1, 4));
        public static VideoPixelFormat Yuv420P8 { get; } =
            new PlanarVideoPixelFormat("yuv420p8", PlanarColorModel.Yuv, 8, 2, 2);
        /// <summary>Planar Y/Cb/Cr with 4:2:0 subsampling. Samples occupy the low ten bits of native-endian unsigned 16-bit words.</summary>
        public static VideoPixelFormat Yuv420P10 { get; } =
            new PlanarVideoPixelFormat("yuv420p10", PlanarColorModel.Yuv, 10, 2, 2);
        public static VideoPixelFormat Yuv420P12 { get; } = new PlanarVideoPixelFormat("yuv420p12", PlanarColorModel.Yuv, 12, 2, 2);
    }
}

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
            new("yuv420p8", new VideoPlaneFormat(1, 1, 1), new VideoPlaneFormat(2, 2, 1), new VideoPlaneFormat(2, 2, 1));
    }
}

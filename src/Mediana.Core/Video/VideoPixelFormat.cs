// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Core.Video
{
    public class VideoPixelFormat : IEquatable<VideoPixelFormat>
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

        /// <summary>Compares sample interpretation and storage layout. Base formats use the ordinal name
        /// as their sample interpretation identifier; planar formats describe it with typed components.</summary>
        public bool Equals(VideoPixelFormat? other) => ReferenceEquals(this, other) ||
            other is not null && GetType() == other.GetType() && EqualsInterpretation(other) &&
            Planes.SequenceEqual(other.Planes);

        protected virtual bool EqualsInterpretation(VideoPixelFormat other) => Name == other.Name;
        protected virtual int InterpretationHashCode() => StringComparer.Ordinal.GetHashCode(Name);

        public override bool Equals(object? obj) => obj is VideoPixelFormat other && Equals(other);

        public override int GetHashCode()
        {
            HashCode hash = new();
            hash.Add(GetType());
            hash.Add(InterpretationHashCode());
            foreach (VideoPlaneFormat plane in Planes)
            {
                hash.Add(plane);
            }
            return hash.ToHashCode();
        }

        public static bool operator ==(VideoPixelFormat? left, VideoPixelFormat? right) =>
            ReferenceEquals(left, right) || left is not null && left.Equals(right);
        public static bool operator !=(VideoPixelFormat? left, VideoPixelFormat? right) => !(left == right);

        /// <summary>Red, green, blue and straight (unassociated) alpha bytes.</summary>
        public static VideoPixelFormat Rgba32 { get; } = new("rgba32", new VideoPlaneFormat(1, 1, 4));
        /// <summary>Cyan, magenta, yellow and black ink coverage bytes: zero is no ink, 255 is full ink. No alpha channel.</summary>
        public static VideoPixelFormat Cmyk32 { get; } = new("cmyk32", new VideoPlaneFormat(1, 1, 4));
        /// <summary>Native-endian IEEE float32 red, green, blue and premultiplied alpha. RGB retains negative and HDR values.</summary>
        public static VideoPixelFormat RgbaFloat32Premultiplied { get; } = new("rgba-float32-premultiplied", new VideoPlaneFormat(1, 1, 16));
        public static VideoPixelFormat Yuv420P8 { get; } =
            new PlanarVideoPixelFormat("yuv420p8", PlanarColorModel.Yuv, 8, 2, 2);
        /// <summary>Planar Y/Cb/Cr with 4:2:0 subsampling. Samples occupy the low ten bits of native-endian unsigned 16-bit words.</summary>
        public static VideoPixelFormat Yuv420P10 { get; } =
            new PlanarVideoPixelFormat("yuv420p10", PlanarColorModel.Yuv, 10, 2, 2);
        public static VideoPixelFormat Yuv422P8 { get; } = new PlanarVideoPixelFormat("yuv422p8", PlanarColorModel.Yuv, 8, 2, 1);
        public static VideoPixelFormat Yuv411P8 { get; } = new PlanarVideoPixelFormat("yuv411p8", PlanarColorModel.Yuv, 8, 4, 1);
        public static VideoPixelFormat Yuv422P10 { get; } = new PlanarVideoPixelFormat("yuv422p10", PlanarColorModel.Yuv, 10, 2, 1);
        public static VideoPixelFormat Yuv444P8 { get; } = new PlanarVideoPixelFormat("yuv444p8", PlanarColorModel.Yuv, 8);
        public static VideoPixelFormat Yuv444P10 { get; } = new PlanarVideoPixelFormat("yuv444p10", PlanarColorModel.Yuv, 10);
        public static VideoPixelFormat GbrP8 { get; } = new PlanarVideoPixelFormat("gbrp8", PlanarColorModel.Gbr, 8);
        public static VideoPixelFormat GbrP10 { get; } = new PlanarVideoPixelFormat("gbrp10", PlanarColorModel.Gbr, 10);
        public static VideoPixelFormat Yuv420P12 { get; } = new PlanarVideoPixelFormat("yuv420p12", PlanarColorModel.Yuv, 12, 2, 2);
    }
}

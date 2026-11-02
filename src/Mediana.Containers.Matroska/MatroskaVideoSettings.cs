// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Matroska
{
    public record MatroskaVideoSettings
    {
        public required ulong PixelWidth { get; init; }
        public required ulong PixelHeight { get; init; }
        public ulong CropBottom { get; init; }
        public ulong CropTop { get; init; }
        public ulong CropLeft { get; init; }
        public ulong CropRight { get; init; }
        public ulong? DisplayWidth { get; init; }
        public ulong? DisplayHeight { get; init; }
        public ulong DisplayUnit { get; init; }
        public ulong Interlaced { get; init; }
        public ulong FieldOrder { get; init; }
        public ulong StereoMode { get; init; }
        public ulong AlphaMode { get; init; }
        public bool HasColourDescription => Colour is not null;
        public bool HasProjection => Projection is not null;
        public MatroskaColourSettings? Colour { get; init; }
        internal EbmlElement? Projection { get; init; }
    }
}

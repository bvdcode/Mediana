// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Matroska
{
    internal record MatroskaVideoSettings
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
        public EbmlElement? Colour { get; init; }
        public EbmlElement? Projection { get; init; }
    }
}

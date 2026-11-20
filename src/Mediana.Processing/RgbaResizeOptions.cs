// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Processing
{
    public record RgbaResizeOptions
    {
        public ImageResizeFilter Filter { get; init; } = ImageResizeFilter.Lanczos3;
        public ResizeColorSpace ColorSpace { get; init; } = ResizeColorSpace.LinearSrgb;
    }
}

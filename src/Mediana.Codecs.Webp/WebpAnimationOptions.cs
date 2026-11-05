// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Webp
{
    /// <summary>Animation canvas and defaults. BackgroundArgb is straight 0xAARRGGBB; a loop count of zero repeats indefinitely.</summary>
    public record WebpAnimationOptions(int CanvasWidth, int CanvasHeight)
    {
        public ushort LoopCount { get; init; }
        public uint BackgroundArgb { get; init; }
        public WebpEncodingOptions Encoding { get; init; } = new();

        internal void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(CanvasWidth);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(CanvasHeight);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(CanvasWidth, 1 << 24);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(CanvasHeight, 1 << 24);
            if ((long)CanvasWidth * CanvasHeight > uint.MaxValue) { throw new ArgumentOutOfRangeException(nameof(CanvasWidth), "WebP canvas area cannot exceed 2^32 - 1 pixels."); }
            ArgumentNullException.ThrowIfNull(Encoding);
            Encoding.Validate();
        }
    }
}

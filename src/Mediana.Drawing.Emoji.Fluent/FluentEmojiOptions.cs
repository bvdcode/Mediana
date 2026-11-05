// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Drawing.Emoji.Fluent
{
    /// <summary>Bounds for the provider's shared interpreted-scene cache. Layouts retain their own resolved graphics.</summary>
    public record FluentEmojiOptions
    {
        public int MaximumCachedGraphics { get; init; } = 128;
    }
}

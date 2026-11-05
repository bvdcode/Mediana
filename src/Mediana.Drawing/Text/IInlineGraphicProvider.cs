// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Drawing
{
    /// <summary>Resolves a complete Unicode text element to an immutable inline graphic.
    /// Returning null leaves the element to the text renderer. Implementations must not consume a prefix.</summary>
    public interface IInlineGraphicProvider
    {
        InlineGraphic? Resolve(ReadOnlySpan<char> textElement, CancellationToken cancellationToken = default);
    }
}

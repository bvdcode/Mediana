// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Presentation
{
    /// <summary>A slide's zero-based position and canonical package part name.</summary>
    public record PresentationSlideInfo(int Index, string PartName);
}

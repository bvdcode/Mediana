// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.DrawingComparison
{
    internal interface IRenderedImage : IDisposable
    {
        void CopyPixelsTo(Span<byte> pixels);
    }
}

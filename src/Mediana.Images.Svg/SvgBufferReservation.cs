// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System;
using System.Threading;

namespace Mediana.Images.Svg
{
    internal class SvgBufferReservation(SvgRenderContext owner, long bytes) : IDisposable
    {
        public void Dispose() => owner.ReleaseReservation(bytes);
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System;
using System.Threading;

namespace Mediana.Drawing
{
    internal class SceneBufferReservation(SceneRenderContext owner, long bytes) : IDisposable
    {
        public void Dispose() => owner.ReleaseReservation(bytes);
    }
}

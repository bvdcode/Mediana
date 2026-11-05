// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using Mediana.Core.Memory;
using Mediana.Drawing;

namespace Mediana.Documents.Pdf
{
    internal class PdfDrawingContext(Matrix3x2 device, int width, int height, MediaBufferAllocator allocator, CancellationToken token) : IDisposable
    {
        private readonly Dictionary<PdfSoftMask, PixelBuffer> _masks = [];
        internal Matrix3x2 Device { get; } = device;
        internal int Width { get; } = width;
        internal int Height { get; } = height;
        internal MediaBufferAllocator Allocator { get; } = allocator;
        internal CancellationToken Token { get; } = token;

        internal PixelBuffer Mask(PdfSoftMask plan)
        {
            Token.ThrowIfCancellationRequested();
            if (_masks.TryGetValue(plan, out PixelBuffer? cached)) { return cached; }
            PixelBuffer pixels = plan.Render(this);
            bool retained = false;
            try
            {
                _masks.Add(plan, pixels);
                retained = true;
                return pixels;
            }
            finally
            {
                if (!retained) { pixels.Dispose(); }
            }
        }

        public void Dispose()
        {
            foreach (PixelBuffer mask in _masks.Values) { mask.Dispose(); }
            _masks.Clear();
        }
    }
}

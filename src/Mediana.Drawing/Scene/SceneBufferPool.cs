// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mediana.Drawing
{
    internal class SceneBufferPool(SceneRenderContext context) : IDisposable
    {
        private readonly Dictionary<PixelBuffer, IDisposable> _buffers = new();
        private readonly HashSet<PixelBuffer> _available = new();

        internal PixelBuffer Rent(int width, int height)
        {
            PixelBuffer? image = _available.FirstOrDefault(buffer => buffer.Width == width && buffer.Height == height);
            if (image is not null)
            {
                _available.Remove(image);
                image.Pixels.Span.Clear();
                return image;
            }

            IDisposable reservation = context.Reserve(width, height, 1);
            bool retained = false;
            try
            {
                image = new PixelBuffer(width, height);
                _buffers.Add(image, reservation);
                retained = true;
                return image;
            }
            finally
            {
                if (!retained)
                {
                    image?.Dispose();
                    reservation.Dispose();
                }
            }
        }

        internal void Return(PixelBuffer image) => _available.Add(image);

        internal void Trim()
        {
            foreach (PixelBuffer image in _available)
            {
                image.Dispose();
                _buffers[image].Dispose();
                _buffers.Remove(image);
            }

            _available.Clear();
        }

        public void Dispose()
        {
            foreach ((PixelBuffer image, IDisposable reservation) in _buffers)
            {
                image.Dispose();
                reservation.Dispose();
            }

            _buffers.Clear();
            _available.Clear();
        }
    }
}

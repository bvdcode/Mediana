// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System;
using System.Threading;

namespace Mediana.Images.Svg
{
    internal class SvgRenderContext : IDisposable
    {
        private long _reserved;
        internal SvgRenderOptions Options { get; }
        internal CancellationToken Token { get; }
        internal SvgBufferPool Buffers { get; }
        internal SvgBufferScope? Scope { get; set; }

        internal SvgRenderContext(SvgRenderOptions options, CancellationToken token)
        {
            Options = options;
            Token = token;
            Buffers = new SvgBufferPool(this);
        }

        internal SvgBufferScope BeginBuffers() => Scope = new SvgBufferScope(this, Scope);

        internal PixelBuffer CreateBuffer(int width, int height)
        {
            PixelBuffer image = Buffers.Rent(width, height);
            Scope!.Add(image);
            return image;
        }

        internal void Release(PixelBuffer image) => Scope!.Release(image);

        internal IDisposable Reserve(int width, int height, int buffers)
        {
            long bytes = checked((long)width * height * 4 * buffers);
            if (bytes > Options.MaximumEffectBytes - _reserved)
            {
                Buffers.Trim();
            }

            if (bytes > Options.MaximumEffectBytes - _reserved)
            {
                throw new InvalidOperationException("SVG effects exceed MaximumEffectBytes.");
            }

            _reserved += bytes;
            return new SvgBufferReservation(this, bytes);
        }

        internal void ReleaseReservation(long bytes) => _reserved -= bytes;

        public void Dispose() => Buffers.Dispose();


    }
}

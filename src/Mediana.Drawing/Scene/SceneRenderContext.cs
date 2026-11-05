// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System;
using System.Threading;
using Mediana.Core.Memory;

namespace Mediana.Drawing
{
    internal class SceneRenderContext : IDisposable
    {
        private long _reserved;
        internal DrawingRenderOptions Options { get; }
        internal CancellationToken Token { get; }
        internal SceneBufferPool Buffers { get; }
        internal MediaBufferAllocator? Allocator { get; }
        internal SceneBufferScope? Scope { get; set; }

        internal SceneRenderContext(DrawingRenderOptions options, CancellationToken token, MediaBufferAllocator? allocator = null)
        {
            Options = options;
            Token = token;
            Allocator = allocator;
            Buffers = new SceneBufferPool(this);
        }

        internal SceneBufferScope BeginBuffers() => Scope = new SceneBufferScope(this, Scope);

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
                throw new InvalidOperationException("Drawing effects exceed MaximumEffectBytes.");
            }

            _reserved += bytes;
            return new SceneBufferReservation(this, bytes);
        }

        internal void ReleaseReservation(long bytes) => _reserved -= bytes;

        public void Dispose() => Buffers.Dispose();


    }
}

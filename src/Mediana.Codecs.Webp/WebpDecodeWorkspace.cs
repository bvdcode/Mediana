// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Codecs.Webp
{
    internal class WebpDecodeWorkspace(MediaBufferAllocator allocator) : IDisposable
    {
        private readonly List<WritableMediaBuffer> buffers = [];
        internal Memory<byte> Allocate(int bytes)
        {
            WritableMediaBuffer buffer = allocator.Allocate(bytes);
            buffers.Add(buffer); return buffer.Memory;
        }
        public void Dispose()
        {
            foreach (WritableMediaBuffer buffer in buffers) { buffer.Dispose(); }
            buffers.Clear();
        }
    }
}

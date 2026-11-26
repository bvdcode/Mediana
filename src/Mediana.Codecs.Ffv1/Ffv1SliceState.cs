// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Codecs.Ffv1
{
    internal class Ffv1SliceState : IDisposable
    {
        private WritableMediaBuffer? context;
        private int lumaSize;
        public Ffv1SliceHeader Header { get; private set; }
        public Span<byte> Luma => context!.Span[..lumaSize];
        public Span<byte> Chroma => context!.Span[lumaSize..];

        public void Prepare(Ffv1SliceHeader header, Ffv1SliceConfiguration configuration, MediaBufferAllocator allocator, bool key)
        {
            if (!key)
            {
                if (context is null || header.X != Header.X || header.Y != Header.Y ||
                    header.Width != Header.Width || header.Height != Header.Height ||
                    header.LumaTable != Header.LumaTable || header.ChromaTable != Header.ChromaTable)
                {
                    throw new NotSupportedException("FFV1 changes slice coding geometry between keyframes.");
                }
                return;
            }
            Ffv1Quantizer luma = configuration.Quantizers[header.LumaTable];
            Ffv1Quantizer chroma = configuration.Quantizers[header.ChromaTable];
            lumaSize = luma.StateSize(configuration.Rice);
            int size = checked(lumaSize + chroma.StateSize(configuration.Rice));
            if (context is null || context.Memory.Length != size)
            {
                context?.Dispose();
                context = allocator.Allocate(size);
            }
            luma.InitializeState(Luma, configuration.Rice);
            chroma.InitializeState(Chroma, configuration.Rice);
            Header = header;
        }

        public void Dispose()
        {
            context?.Dispose();
            context = null;
        }
    }
}

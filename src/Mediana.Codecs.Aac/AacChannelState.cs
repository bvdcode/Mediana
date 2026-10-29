// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Aac
{
    internal class AacChannelState(MediaBufferAllocator allocator) : IDisposable
    {
        private const int BookOffset = 4096;
        private const int ScaleOffset = BookOffset + 512;
        private const int SpectrumOffset = ScaleOffset + 512 * sizeof(int);
        private const int OverlapOffset = SpectrumOffset + 1024 * sizeof(double);
        private readonly WritableMediaBuffer storage = allocator.Allocate(OverlapOffset + 1024 * sizeof(double));

        public AacWindowInfo Info { get; set; } = null!;
        public int PreviousShape { get; set; }
        public List<AacTnsFilter> TnsFilters { get; } = [];
        public Span<int> Quantized => MemoryMarshal.Cast<byte, int>(storage.Memory.Span[..BookOffset]);
        public Span<byte> Books => storage.Memory.Span.Slice(BookOffset, 512);
        public Span<int> Scales => MemoryMarshal.Cast<byte, int>(storage.Memory.Span.Slice(ScaleOffset, 512 * sizeof(int)));
        public Span<double> Spectrum => MemoryMarshal.Cast<byte, double>(storage.Memory.Span.Slice(SpectrumOffset, 1024 * sizeof(double)));
        public Span<double> Overlap => MemoryMarshal.Cast<byte, double>(storage.Memory.Span[OverlapOffset..]);

        public void ClearFrame()
        {
            storage.Memory.Span[..OverlapOffset].Clear();
            TnsFilters.Clear();
        }

        public void Dispose() => storage.Dispose();
    }
}

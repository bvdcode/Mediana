// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Ac3
{
    internal class Ac3Workspace(MediaBufferAllocator allocator) : IDisposable
    {
        private const int SpectralBytes = 7 * 256 * sizeof(double);
        private const int OverlapOffset = SpectralBytes;
        private const int CoordinatesOffset = OverlapOffset + 6 * 256 * sizeof(double);
        private const int DeltaOffset = CoordinatesOffset + 5 * 18 * sizeof(double);
        private const int ExponentsOffset = DeltaOffset + 7 * 50 * sizeof(int);
        private const int BapOffset = ExponentsOffset + 7 * 256;
        private readonly WritableMediaBuffer storage = allocator.Allocate(BapOffset + 7 * 256);
        public Span<double> Spectrum(int channel) => MemoryMarshal.Cast<byte, double>(storage.Span.Slice(channel * 256 * sizeof(double), 256 * sizeof(double)));
        public Span<double> Overlap(int channel) => MemoryMarshal.Cast<byte, double>(storage.Span.Slice(OverlapOffset + channel * 256 * sizeof(double), 256 * sizeof(double)));
        public Span<double> Coordinates(int channel) => MemoryMarshal.Cast<byte, double>(storage.Span.Slice(CoordinatesOffset + channel * 18 * sizeof(double), 18 * sizeof(double)));
        public Span<int> Delta(int channel) => MemoryMarshal.Cast<byte, int>(storage.Span.Slice(DeltaOffset + channel * 50 * sizeof(int), 50 * sizeof(int)));
        public Span<byte> Exponents(int channel) => storage.Span.Slice(ExponentsOffset + channel * 256, 256);
        public Span<byte> Bap(int channel) => storage.Span.Slice(BapOffset + channel * 256, 256);
        public void Reset() => storage.Span.Clear();
        public void Dispose() => storage.Dispose();
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.InteropServices;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Aac
{
    internal class AacEncoderWorkspace : IDisposable
    {
        private const int ChannelBytes = 2048 * sizeof(double) + 1024 * sizeof(double) + 1024 * sizeof(int) + 128;
        private readonly WritableMediaBuffer storage;
        private readonly int channels;

        public AacEncoderWorkspace(MediaBufferAllocator allocator, int channels)
        {
            this.channels = channels;
            storage = allocator.Allocate(channels * ChannelBytes + 4096 * sizeof(double));
            storage.Memory.Span.Clear();
        }

        public Span<double> Input(int channel) => MemoryMarshal.Cast<byte, double>(storage.Memory.Span.Slice(channel * ChannelBytes, 2048 * sizeof(double)));
        public Span<double> Spectrum(int channel) => MemoryMarshal.Cast<byte, double>(storage.Memory.Span.Slice(channel * ChannelBytes + 2048 * sizeof(double), 1024 * sizeof(double)));
        public Span<int> Quantized(int channel) => MemoryMarshal.Cast<byte, int>(storage.Memory.Span.Slice(channel * ChannelBytes + 3072 * sizeof(double), 1024 * sizeof(int)));
        public Span<byte> Books(int channel) => storage.Memory.Span.Slice((channel + 1) * ChannelBytes - 128, 64);
        public Span<byte> Scales(int channel) => storage.Memory.Span.Slice((channel + 1) * ChannelBytes - 64, 64);
        public Span<double> Window => MemoryMarshal.Cast<byte, double>(storage.Memory.Span.Slice(channels * ChannelBytes, 2048 * sizeof(double)));
        public Span<double> Folded => MemoryMarshal.Cast<byte, double>(storage.Memory.Span.Slice(channels * ChannelBytes + 2048 * sizeof(double), 1024 * sizeof(double)));
        public Span<Complex> Fourier => MemoryMarshal.Cast<byte, Complex>(storage.Memory.Span.Slice(channels * ChannelBytes + 3072 * sizeof(double), 1024 * sizeof(double)));

        public void Dispose() => storage.Dispose();
    }
}

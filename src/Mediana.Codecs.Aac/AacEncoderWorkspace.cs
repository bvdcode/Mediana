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

        public Span<double> Input(int channel) => MemoryMarshal.Cast<byte, double>(storage.Memory.Span.Slice(channel * 2048 * sizeof(double), 2048 * sizeof(double)));
        public Span<double> Spectra => MemoryMarshal.Cast<byte, double>(storage.Memory.Span.Slice(channels * 2048 * sizeof(double), channels * 1024 * sizeof(double)));
        public Span<double> Spectrum(int channel) => Spectra.Slice(channel * 1024, 1024);
        public Span<int> Quantized => MemoryMarshal.Cast<byte, int>(storage.Memory.Span.Slice(channels * 3072 * sizeof(double), channels * 1024 * sizeof(int)));
        public Span<byte> Books => storage.Memory.Span.Slice(channels * (3072 * sizeof(double) + 1024 * sizeof(int)), channels * 64);
        public Span<byte> Scales => storage.Memory.Span.Slice(channels * (3072 * sizeof(double) + 1024 * sizeof(int) + 64), channels * 64);
        public Span<double> Window => MemoryMarshal.Cast<byte, double>(storage.Memory.Span.Slice(channels * ChannelBytes, 2048 * sizeof(double)));
        public Span<double> Folded => MemoryMarshal.Cast<byte, double>(storage.Memory.Span.Slice(channels * ChannelBytes + 2048 * sizeof(double), 1024 * sizeof(double)));
        public Span<Complex> Fourier => MemoryMarshal.Cast<byte, Complex>(storage.Memory.Span.Slice(channels * ChannelBytes + 3072 * sizeof(double), 1024 * sizeof(double)));

        public void Dispose() => storage.Dispose();
    }
}

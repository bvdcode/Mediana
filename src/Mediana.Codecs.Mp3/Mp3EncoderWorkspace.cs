// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Mp3
{
    internal class Mp3EncoderWorkspace : IDisposable
    {
        private const int ChannelDoubles = 512 + 576;
        private readonly WritableMediaBuffer storage;
        private readonly int channels;
        private readonly int frameSamples;

        public Mp3EncoderWorkspace(MediaBufferAllocator allocator, int channels, int frameSamples)
        {
            this.channels = channels;
            this.frameSamples = frameSamples;
            storage = allocator.Allocate(channels * (ChannelDoubles * sizeof(double) + frameSamples * (sizeof(float) + sizeof(int))));
            storage.Memory.Span.Clear();
        }

        public Span<double> History(int channel) => MemoryMarshal.Cast<byte, double>(storage.Memory.Span.Slice(channel * ChannelDoubles * sizeof(double), 512 * sizeof(double)));
        public Span<double> Previous(int channel) => MemoryMarshal.Cast<byte, double>(storage.Memory.Span.Slice((channel * ChannelDoubles + 512) * sizeof(double), 576 * sizeof(double)));
        public Span<float> Pcm => MemoryMarshal.Cast<byte, float>(storage.Memory.Span.Slice(channels * ChannelDoubles * sizeof(double), channels * frameSamples * sizeof(float)));
        public Span<int> Quantized => MemoryMarshal.Cast<byte, int>(storage.Memory.Span.Slice(channels * (ChannelDoubles * sizeof(double) + frameSamples * sizeof(float))));
        public void Dispose() => storage.Dispose();
    }
}

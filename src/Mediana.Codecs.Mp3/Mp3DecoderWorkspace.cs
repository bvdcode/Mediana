// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Audio;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Mp3
{
    internal class Mp3DecoderWorkspace : IDisposable
    {
        private const int ChannelBytes = 1152 * sizeof(double) + 1024 * sizeof(float);
        private readonly WritableMediaBuffer storage;
        private readonly int channels;

        public Mp3DecoderWorkspace(MediaBufferAllocator allocator, Mp3FrameHeader header, int maxFrameBytes)
        {
            channels = header.ChannelCount;
            storage = allocator.Allocate(channels * ChannelBytes + 576 * sizeof(double));
            bool succeeded = false;
            try
            {
                Reservoir = new Mp3BitReservoir(allocator, header.Version, maxFrameBytes);
                storage.Memory.Span.Clear();
                succeeded = true;
            }
            finally
            {
                if (!succeeded)
                {
                    storage.Dispose();
                }
            }
        }

        public Mp3BitReservoir Reservoir { get; }
        public int[] SynthesisPositions { get; } = new int[2];
        public Span<double> Spectrum(int channel) => MemoryMarshal.Cast<byte, double>(storage.Memory.Span.Slice(channel * ChannelBytes, 576 * sizeof(double)));
        public Span<double> Overlap(int channel) => MemoryMarshal.Cast<byte, double>(storage.Memory.Span.Slice(channel * ChannelBytes + 576 * sizeof(double), 576 * sizeof(double)));
        public Span<float> History(int channel) => MemoryMarshal.Cast<byte, float>(storage.Memory.Span.Slice(channel * ChannelBytes + 1152 * sizeof(double), 1024 * sizeof(float)));
        public Span<double> Subbands => MemoryMarshal.Cast<byte, double>(storage.Memory.Span[(channels * ChannelBytes)..]);

        public void Dispose()
        {
            Reservoir.Dispose();
            storage.Dispose();
        }
    }
}

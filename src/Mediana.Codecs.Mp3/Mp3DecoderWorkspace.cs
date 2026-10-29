// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Audio;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Mp3
{
    internal class Mp3DecoderWorkspace : IDisposable
    {
        private const int ChannelValues = 576 + 576 + 1024;
        private readonly WritableMediaBuffer storage;
        private readonly int channels;

        public Mp3DecoderWorkspace(MediaBufferAllocator allocator, Mp3FrameHeader header, int maxFrameBytes)
        {
            channels = header.ChannelCount;
            storage = allocator.Allocate((channels * ChannelValues + 576) * sizeof(double));
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
        public Span<double> Spectrum(int channel) => Values.Slice(channel * ChannelValues, 576);
        public Span<double> Overlap(int channel) => Values.Slice(channel * ChannelValues + 576, 576);
        public Span<double> History(int channel) => Values.Slice(channel * ChannelValues + 1152, 1024);
        public Span<double> Subbands => Values.Slice(channels * ChannelValues, 576);
        private Span<double> Values => MemoryMarshal.Cast<byte, double>(storage.Memory.Span);

        public void Dispose()
        {
            Reservoir.Dispose();
            storage.Dispose();
        }
    }
}

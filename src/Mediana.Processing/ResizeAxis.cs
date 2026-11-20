// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Processing
{
    internal class ResizeAxis : IDisposable
    {
        private readonly WritableMediaBuffer storage;
        private readonly int length;

        public ResizeAxis(int source, int destination, ImageResizeFilter filter, MediaBufferAllocator allocator, CancellationToken token)
        {
            ChromaUpsamplingFilter kernel = filter switch
            {
                ImageResizeFilter.Bilinear => ChromaUpsamplingFilter.Bilinear,
                ImageResizeFilter.Bicubic => ChromaUpsamplingFilter.Bicubic,
                ImageResizeFilter.Lanczos3 => ChromaUpsamplingFilter.Lanczos3,
                _ => throw new ArgumentOutOfRangeException(nameof(filter))
            };
            double scale = Math.Max(1, (double)source / destination);
            double radius = ReconstructionKernel.Taps(kernel) * 0.5 * scale;
            Capacity = (int)Math.Min(source, Math.Ceiling(2 * radius) + 1);
            length = destination;
            storage = allocator.Allocate(checked(destination * (2 + Capacity) * sizeof(float)));
            bool complete = false;
            try
            {
                for (int x = 0; x < destination; x++)
                {
                    token.ThrowIfCancellationRequested();
                    double center = (x + 0.5) * source / destination - 0.5;
                    int first = (int)Math.Max(0, Math.Ceiling(center - radius));
                    int last = (int)Math.Min(source - 1, Math.Floor(center + radius));
                    Starts[x] = first;
                    Counts[x] = last - first + 1;
                    Span<float> weights = Weights(x);
                    double sum = 0;
                    for (int i = 0; i < weights.Length; i++)
                    {
                        if ((i & 255) == 0) { token.ThrowIfCancellationRequested(); }
                        double weight = ReconstructionKernel.Weight(kernel, (first + i - center) / scale);
                        weights[i] = (float)weight;
                        sum += weight;
                    }
                    for (int i = 0; i < weights.Length; i++) { weights[i] /= (float)sum; }
                }
                complete = true;
            }
            finally
            {
                if (!complete) { storage.Dispose(); }
            }
        }

        public int Capacity { get; }
        public Span<int> Starts => MemoryMarshal.Cast<byte, int>(storage.Memory.Span)[..length];
        private Span<int> Counts => MemoryMarshal.Cast<byte, int>(storage.Memory.Span).Slice(length, length);
        public Span<float> Weights(int index) => MemoryMarshal.Cast<byte, float>(storage.Memory.Span)
            .Slice(checked(length * 2 + index * Capacity), Counts[index]);
        public void Dispose() => storage.Dispose();
    }
}

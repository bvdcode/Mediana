// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Jpeg
{
    internal class JpegProgression : IDisposable
    {
        private readonly int[] approximation = new int[64];
        private WritableMediaBuffer? coefficients;

        internal JpegProgression(MediaBufferAllocator? allocator, int samples)
        {
            Array.Fill(approximation, -1);
            if (allocator is not null)
            {
                coefficients = allocator.Allocate(checked(samples * sizeof(int)));
                coefficients.Memory.Span.Clear();
            }
        }

        internal void Validate(JpegScanParameters scan)
        {
            if (scan.First != 0 && approximation[0] < 0)
            {
                throw new InvalidDataException("A progressive JPEG component requires its first DC scan before AC scans.");
            }
            for (int i = scan.First; i <= scan.Last; i++)
            {
                int preceding = scan.High == 0 ? -1 : scan.High;
                if (approximation[i] != preceding)
                {
                    throw new InvalidDataException("Progressive JPEG coefficient bands are repeated or refined out of order.");
                }
            }
        }

        internal void Complete(JpegScanParameters scan) =>
            approximation.AsSpan(scan.First, scan.Last - scan.First + 1).Fill(scan.Low);

        internal Span<int> Block(int x, int y, int stride) =>
            MemoryMarshal.Cast<byte, int>(coefficients!.Memory.Span).Slice((y * (stride / 8) + x) * 64, 64);

        internal void Reconstruct(JpegComponent component, int[] quantization, MediaBufferAllocator allocator, CancellationToken token)
        {
            component.Samples = allocator.Allocate(checked(component.Stride * component.PaddedHeight));
            for (int y = 0; y < component.PaddedHeight / 8; y++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = 0; x < component.Stride / 8; x++)
                {
                    Span<byte> output = component.Samples.Memory.Span[(y * 8 * component.Stride + x * 8)..];
                    JpegInverseTransform.Write(Block(x, y, component.Stride), quantization, output, component.Stride);
                }
            }
            Dispose();
        }

        public void Dispose()
        {
            coefficients?.Dispose();
            coefficients = null;
        }
    }
}

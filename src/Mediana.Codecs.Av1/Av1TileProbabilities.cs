// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal static class Av1TileProbabilities
    {
        internal static void Initialize(Span<ushort> target, int quantizer, ReadOnlySpan<ushort> reference = default)
        {
            if (target.Length != Av1DefaultCdfs.Count)
            {
                throw new ArgumentException("The AV1 probability workspace has an invalid extent.", nameof(target));
            }
            if (reference.IsEmpty)
            {
                Av1DefaultCdfs.Values.CopyTo(target);
                Av1CoefficientCdfs.Initialize(target, quantizer);
            }
            else
            {
                if (reference.Length != target.Length)
                {
                    throw new InvalidDataException("The AV1 reference probability snapshot has an invalid extent.");
                }
                reference.CopyTo(target);
                foreach (int offset in Av1DefaultCdfs.CounterOffsets) { target[offset] = 0; }
            }
        }
    }
}

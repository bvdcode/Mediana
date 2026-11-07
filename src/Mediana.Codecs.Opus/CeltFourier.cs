// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Transforms;

namespace Mediana.Codecs.Opus
{
    internal static class CeltFourier
    {
        public static void Transform(Span<float> workspace, ReadOnlySpan<float> rotations)
        {
            ReadOnlySpan<byte> radices = workspace.Length switch
            {
                120 => [5, 3, 4],
                240 => [5, 3, 2, 4],
                480 => [5, 3, 4, 4],
                960 => [5, 3, 2, 4, 4],
                _ => throw new ArgumentException("The CELT Fourier size is invalid.", nameof(workspace))
            };
            ComplexFourier.Transform(workspace, rotations, radices);
        }
    }
}

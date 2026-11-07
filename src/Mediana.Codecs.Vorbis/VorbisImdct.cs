// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.InteropServices;
using Mediana.Codecs.Transforms;

namespace Mediana.Codecs.Vorbis
{
    internal static class VorbisImdct
    {
        public static void Transform(VorbisTransformPlan plan, ReadOnlySpan<float> spectrum, Span<float> output,
            Span<float> workspace, CancellationToken token = default)
        {
            int bins = plan.BlockSize / 2;
            if (spectrum.Length != bins || output.Length < plan.BlockSize || workspace.Length < bins ||
                spectrum.Overlaps(output) || spectrum.Overlaps(workspace) || output.Overlaps(workspace))
            {
                throw new ArgumentException("The Vorbis inverse transform requires complete, nonoverlapping input, output and workspace.");
            }
            token.ThrowIfCancellationRequested();
            Span<Vector2> values = MemoryMarshal.Cast<float, Vector2>(workspace[..bins]);
            ReadOnlySpan<Vector2> pre = plan.PreRotation;
            ReadOnlySpan<ushort> permutation = plan.Permutation;
            for (int index = 0; index < values.Length; index++)
            {
                float even = spectrum[2 * index], odd = spectrum[bins - 1 - 2 * index];
                Vector2 phase = pre[index];
                values[permutation[index]] = new(even * phase.X - odd * phase.Y, even * phase.Y + odd * phase.X);
            }
            ComplexFourier.Transform(workspace[..bins], plan.Rotations, plan.Radices);
            ReadOnlySpan<Vector2> post = plan.PostRotation;
            for (int index = 0; index < values.Length; index++)
            {
                Vector2 value = values[index], phase = post[index];
                Write(output, bins, 2 * index, value.X * phase.X - value.Y * phase.Y);
                Write(output, bins, bins - 1 - 2 * index, -(value.X * phase.Y + value.Y * phase.X));
            }
        }

        private static void Write(Span<float> output, int bins, int coefficient, float value)
        {
            output[3 * bins / 2 - 1 - coefficient] = -value;
            if (coefficient < bins / 2) { output[3 * bins / 2 + coefficient] = -value; }
            else { output[coefficient - bins / 2] = value; }
        }
    }
}

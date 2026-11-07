// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal static class CeltImdct
    {
        /// <summary>The first half-overlap contains the previous transform's tail; the output retains its new tail.</summary>
        public static void Transform(CeltTransformPlan plan, ReadOnlySpan<float> spectrum, Span<float> output,
            Span<float> workspace, int frameShift, int stride, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentOutOfRangeException.ThrowIfNegative(frameShift);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(frameShift, 3);
            if (stride is not (1 or 2 or 4 or 8)) { throw new ArgumentOutOfRangeException(nameof(stride)); }
            int size = CeltBandTables.ShortTransformSize << frameShift;
            int overlap = CeltTransformPlan.Overlap / 2;
            ArgumentOutOfRangeException.ThrowIfLessThan(spectrum.Length, (size - 1) * stride + 1, nameof(spectrum));
            ArgumentOutOfRangeException.ThrowIfLessThan(output.Length, size + overlap, nameof(output));
            ArgumentOutOfRangeException.ThrowIfLessThan(workspace.Length, size, nameof(workspace));
            if (spectrum.Overlaps(output) || spectrum.Overlaps(workspace) || output.Overlaps(workspace))
            {
                throw new ArgumentException("CELT inverse-transform input, output and workspace must not overlap.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            ReadOnlySpan<float> phase = plan.Phases(frameShift), window = plan.Window, rotations = plan.FourierRotations;
            ReadOnlySpan<ushort> permutation = plan.Permutation(frameShift);
            Span<float> transformed = workspace[..size];
            for (int at = 0; at < size / 2; at++)
            {
                float even = spectrum[2 * at * stride], odd = spectrum[(size - 1 - 2 * at) * stride];
                float cosine = phase[2 * at], sine = phase[2 * at + 1];
                int destination = 2 * permutation[at];
                transformed[destination] = even * cosine + odd * sine;
                transformed[destination + 1] = odd * cosine - even * sine;
            }
            CeltFourier.Transform(transformed, rotations);
            for (int at = 0; at < size / 2; at++)
            {
                float real = transformed[2 * at], imaginary = transformed[2 * at + 1];
                float cosine = phase[2 * at], sine = phase[2 * at + 1];
                output[overlap + 2 * at] = imaginary * cosine - real * sine;
                output[overlap + size - 1 - 2 * at] = -imaginary * sine - real * cosine;
            }
            for (int at = 0; at < overlap; at++)
            {
                int opposite = CeltTransformPlan.Overlap - 1 - at;
                float previous = output[at], next = output[opposite];
                output[at] = previous * window[opposite] - next * window[at];
                output[opposite] = previous * window[at] + next * window[opposite];
            }
        }
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Mp4
{
    /// <summary>Declared roll/prol sample groups. Distances count encoded samples, not PCM samples.
    /// Negative distances require preceding samples; positive distances recover after the marked sample.
    /// A declaration does not prove a codec restart is supported.</summary>
    public class Mp4SampleRecoveryGroup
    {
        private readonly int[] ends;
        private readonly short?[] distances;

        internal Mp4SampleRecoveryGroup(Mp4RecoveryKind kind, int sampleCount, short? defaultDistance,
            IReadOnlyList<(int End, short? Distance)> runs)
        {
            Kind = kind;
            SampleCount = sampleCount;
            DefaultDistance = defaultDistance;
            ends = runs.Select(run => run.End).ToArray();
            distances = runs.Select(run => run.Distance).ToArray();
        }

        public Mp4RecoveryKind Kind { get; }
        public int SampleCount { get; }
        public short? DefaultDistance { get; }

        /// <summary>Returns the declared signed distance at a zero-based decode-order sample index,
        /// or null when no group applies. Explicit group index zero overrides the default.</summary>
        public short? GetDistance(int sampleIndex)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(sampleIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(sampleIndex, SampleCount);
            int index = Array.BinarySearch(ends, sampleIndex + 1);
            if (index < 0)
            {
                index = ~index;
            }
            return index < distances.Length ? distances[index] : DefaultDistance;
        }
    }
}

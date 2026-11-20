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
            IReadOnlyList<(int End, short? Distance)> runs, short[]? descriptions = null)
        {
            Descriptions = descriptions ?? [];
            Kind = kind;
            SampleCount = sampleCount;
            DefaultDistance = defaultDistance;
            ends = runs.Select(run => run.End).ToArray();
            distances = runs.Select(run => run.Distance).ToArray();
        }

        internal ReadOnlyMemory<short> Descriptions { get; }

        internal void AppendRuns(List<(int End, short? Distance)> runs, int start)
        {
            for (int index = 0; index < ends.Length; index++)
            {
                Append(runs, checked(start + ends[index]), distances[index]);
            }
            if (ends.Length == 0 || ends[^1] < SampleCount)
            {
                Append(runs, checked(start + SampleCount), DefaultDistance);
            }
        }

        internal static void Append(List<(int End, short? Distance)> runs, int end, short? distance)
        {
            if (end == (runs.Count == 0 ? 0 : runs[^1].End))
            {
                return;
            }
            if (runs.Count != 0 && runs[^1].Distance == distance)
            {
                runs[^1] = (end, distance);
            }
            else
            {
                runs.Add((end, distance));
            }
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

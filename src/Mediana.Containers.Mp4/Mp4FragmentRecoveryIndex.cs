// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Mp4
{
    internal class Mp4FragmentRecoveryIndex
    {
        private readonly Dictionary<Mp4RecoveryKind, List<(int End, short? Distance)>> runs = [];
        private int sampleCount;

        public Mp4FragmentRecoveryIndex(int initialSamples, IReadOnlyList<Mp4SampleRecoveryGroup> groups) => Add(initialSamples, groups);

        public void Add(int count, IReadOnlyList<Mp4SampleRecoveryGroup> groups)
        {
            int end = checked(sampleCount + count);
            foreach (Mp4SampleRecoveryGroup group in groups)
            {
                if (!runs.TryGetValue(group.Kind, out List<(int End, short? Distance)>? mapping))
                {
                    mapping = [];
                    Mp4SampleRecoveryGroup.Append(mapping, sampleCount, null);
                    runs.Add(group.Kind, mapping);
                }
                group.AppendRuns(mapping, sampleCount);
            }
            foreach (KeyValuePair<Mp4RecoveryKind, List<(int End, short? Distance)>> mapping in runs)
            {
                if (!groups.Any(group => group.Kind == mapping.Key))
                {
                    Mp4SampleRecoveryGroup.Append(mapping.Value, end, null);
                }
            }
            sampleCount = end;
        }

        public IReadOnlyList<Mp4SampleRecoveryGroup> Build() => Array.AsReadOnly(runs
            .Select(mapping => new Mp4SampleRecoveryGroup(mapping.Key, sampleCount, null, mapping.Value)).ToArray());
    }
}

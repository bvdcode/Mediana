// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Hevc
{
    internal class HevcReferenceSet(HevcReferenceEntry[] before, HevcReferenceEntry[] after, HevcReferenceEntry[] longTerm)
    {
        public HevcReferenceLists CreateLists(HevcSliceHeader slice) => CreateLists(slice.ActiveReferences0,
            slice.ActiveReferences1, slice.List0Entries, slice.List1Entries);

        internal HevcReferenceLists CreateLists(int count0, int count1, IReadOnlyList<int> modification0, IReadOnlyList<int> modification1)
        {
            HevcReferenceEntry[] first = [.. before, .. after, .. longTerm], second = [.. after, .. before, .. longTerm];
            return new(Build(first, count0, modification0), Build(second, count1, modification1));
        }

        private static HevcReferenceEntry[] Build(HevcReferenceEntry[] source, int count, IReadOnlyList<int> modification)
        {
            if (count is < 0 or > 15 || (count != 0 && source.Length == 0) ||
                modification.Count != 0 && modification.Count != count)
            { throw new InvalidDataException("Invalid HEVC active reference list."); }
            HevcReferenceEntry[] result = new HevcReferenceEntry[count];
            for (int i = 0; i < count; i++)
            {
                int index = modification.Count == 0 ? i % source.Length : modification[i];
                if ((uint)index >= source.Length) { throw new InvalidDataException("Invalid HEVC reference list modification."); }
                result[i] = source[index];
            }
            return result;
        }
    }
}

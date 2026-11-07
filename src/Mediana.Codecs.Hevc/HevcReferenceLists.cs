// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Hevc
{
    internal class HevcReferenceLists(HevcReferenceEntry[] list0, HevcReferenceEntry[] list1)
    {
        public IReadOnlyList<HevcReferenceEntry> List0 { get; } = Array.AsReadOnly(list0);
        public IReadOnlyList<HevcReferenceEntry> List1 { get; } = Array.AsReadOnly(list1);

        public HevcReferenceEntry Get(int list, int index) => list switch
        {
            0 => List0[index],
            1 => List1[index],
            _ => throw new ArgumentOutOfRangeException(nameof(list))
        };

        public bool AllReferencesInPast(int order)
        {
            foreach (HevcReferenceEntry entry in List0) { if (entry.Picture.Order > order) { return false; } }
            foreach (HevcReferenceEntry entry in List1) { if (entry.Picture.Order > order) { return false; } }
            return true;
        }

        public HevcMotionSample Create(int reference0, HevcMotionVector motion0, int reference1, HevcMotionVector motion1)
        {
            byte flags = HevcMotionSample.Available;
            int order0 = 0, order1 = 0;
            if (reference0 >= 0)
            {
                HevcReferenceEntry entry = List0[reference0]; order0 = entry.Picture.Order;
                flags |= HevcMotionSample.List0;
                if (entry.LongTerm) { flags |= HevcMotionSample.LongTerm0; }
            }
            if (reference1 >= 0)
            {
                HevcReferenceEntry entry = List1[reference1]; order1 = entry.Picture.Order;
                flags |= HevcMotionSample.List1;
                if (entry.LongTerm) { flags |= HevcMotionSample.LongTerm1; }
            }
            return new(order0, order1, motion0, motion1, checked((sbyte)reference0), checked((sbyte)reference1), flags);
        }
    }
}

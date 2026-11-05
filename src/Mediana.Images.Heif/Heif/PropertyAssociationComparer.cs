// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Images.Heif.Container
{
    internal class PropertyAssociationComparer : IEqualityComparer<IReadOnlyList<PropertyAssociation>>
    {
        public bool Equals(IReadOnlyList<PropertyAssociation>? first, IReadOnlyList<PropertyAssociation>? second)
        {
            if (ReferenceEquals(first, second)) { return true; }
            if (first is null || second is null || first.Count != second.Count) { return false; }
            for (int index = 0; index < first.Count; index++)
            {
                if (first[index].Index != second[index].Index || first[index].Essential != second[index].Essential) { return false; }
            }
            return true;
        }

        public int GetHashCode(IReadOnlyList<PropertyAssociation> associations)
        {
            HashCode hash = new();
            foreach (PropertyAssociation association in associations)
            {
                hash.Add(association.Index);
                hash.Add(association.Essential);
            }
            return hash.ToHashCode();
        }
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Vp9
{
    internal static class Vp9ReferenceContext
    {
        internal static int IsInter(Vp9BlockInfo above, Vp9BlockInfo left, bool hasAbove, bool hasLeft)
        {
            if (hasAbove && hasLeft) { return !above.Inter && !left.Inter ? 3 : (!above.Inter || !left.Inter ? 1 : 0); }
            if (hasAbove) { return above.Inter ? 0 : 2; }
            if (hasLeft) { return left.Inter ? 0 : 2; }
            return 0;
        }

        internal static int CompoundMode(Vp9BlockInfo above, Vp9BlockInfo left, bool hasAbove, bool hasLeft, int fixedReference)
        {
            if (hasAbove && hasLeft)
            {
                if (above.ReferenceOne == 0 && left.ReferenceOne == 0) { return B(above.ReferenceZero == fixedReference) ^ B(left.ReferenceZero == fixedReference); }
                if (above.ReferenceOne == 0) { return 2 + B(above.ReferenceZero == fixedReference || !above.Inter); }
                if (left.ReferenceOne == 0) { return 2 + B(left.ReferenceZero == fixedReference || !left.Inter); }
                return 4;
            }
            if (!hasAbove && !hasLeft) { return 1; }
            Vp9BlockInfo neighbor = hasAbove ? above : left;
            return neighbor.ReferenceOne == 0 ? B(neighbor.ReferenceZero == fixedReference) : 3;
        }

        internal static int CompoundReference(Vp9BlockInfo above, Vp9BlockInfo left, bool hasAbove, bool hasLeft,
            int variableIndex, int fixedReference, int variableZero, int variableOne)
        {
            if (!hasAbove || !hasLeft)
            {
                if (!hasAbove && !hasLeft) { return 2; }
                Vp9BlockInfo neighbor = hasAbove ? above : left;
                if (!neighbor.Inter) { return 2; }
                return neighbor.ReferenceOne == 0 ? 3 * B(neighbor.ReferenceZero != variableOne) : 4 * B(neighbor.Reference(variableIndex) != variableOne);
            }
            if (!above.Inter && !left.Inter) { return 2; }
            if (!above.Inter || !left.Inter)
            {
                Vp9BlockInfo neighbor = above.Inter ? above : left;
                return 1 + 2 * B((neighbor.ReferenceOne == 0 ? neighbor.ReferenceZero : neighbor.Reference(variableIndex)) != variableOne);
            }
            bool aboveSingle = above.ReferenceOne == 0, leftSingle = left.ReferenceOne == 0;
            int a = aboveSingle ? above.ReferenceZero : above.Reference(variableIndex), l = leftSingle ? left.ReferenceZero : left.Reference(variableIndex);
            if (a == l && a == variableOne) { return 0; }
            if (aboveSingle && leftSingle)
            {
                if (a == fixedReference && l == variableZero || l == fixedReference && a == variableZero) { return 4; }
                return a == l ? 3 : 1;
            }
            if (aboveSingle || leftSingle)
            {
                int compound = leftSingle ? a : l, single = aboveSingle ? a : l;
                if (compound == variableOne && single != variableOne) { return 1; }
                if (single == variableOne && compound != variableOne) { return 2; }
                return 4;
            }
            return a == l ? 4 : 2;
        }

        internal static int SingleLast(Vp9BlockInfo above, Vp9BlockInfo left, bool hasAbove, bool hasLeft)
        {
            if (!hasAbove || !hasLeft)
            {
                if (!hasAbove && !hasLeft) { return 2; }
                return LastNeighbor(hasAbove ? above : left);
            }
            if (!above.Inter && !left.Inter) { return 2; }
            if (!above.Inter || !left.Inter) { return LastNeighbor(above.Inter ? above : left); }
            bool a = above.ReferenceOne == 0, l = left.ReferenceOne == 0;
            if (a && l) { return 2 * B(above.ReferenceZero == 1) + 2 * B(left.ReferenceZero == 1); }
            if (!a && !l) { return 1 + B(Has(above, 1) || Has(left, 1)); }
            Vp9BlockInfo single = a ? above : left, compound = a ? left : above;
            return single.ReferenceZero == 1 ? 3 + B(Has(compound, 1)) : B(Has(compound, 1));
        }

        internal static int SingleGolden(Vp9BlockInfo above, Vp9BlockInfo left, bool hasAbove, bool hasLeft)
        {
            if (!hasAbove || !hasLeft)
            {
                if (!hasAbove && !hasLeft) { return 2; }
                Vp9BlockInfo neighbor = hasAbove ? above : left;
                if (!neighbor.Inter || neighbor.ReferenceOne == 0 && neighbor.ReferenceZero == 1) { return 2; }
                return neighbor.ReferenceOne == 0 ? 4 * B(neighbor.ReferenceZero == 2) : 3 * B(Has(neighbor, 2));
            }
            if (!above.Inter && !left.Inter) { return 2; }
            if (!above.Inter || !left.Inter)
            {
                Vp9BlockInfo neighbor = above.Inter ? above : left;
                if (neighbor.ReferenceOne != 0) { return 1 + 2 * B(Has(neighbor, 2)); }
                return neighbor.ReferenceZero == 1 ? 3 : 4 * B(neighbor.ReferenceZero == 2);
            }
            bool a = above.ReferenceOne == 0, l = left.ReferenceOne == 0;
            if (a && l)
            {
                if (above.ReferenceZero == 1 && left.ReferenceZero == 1) { return 3; }
                if (above.ReferenceZero == 1) { return 4 * B(left.ReferenceZero == 2); }
                if (left.ReferenceZero == 1) { return 4 * B(above.ReferenceZero == 2); }
                return 2 * B(above.ReferenceZero == 2) + 2 * B(left.ReferenceZero == 2);
            }
            if (!a && !l) { return above.ReferenceZero == left.ReferenceZero && above.ReferenceOne == left.ReferenceOne ? 3 * B(Has(above, 2)) : 2; }
            Vp9BlockInfo single = a ? above : left, compound = a ? left : above;
            return single.ReferenceZero switch
            {
                2 => 3 + B(Has(compound, 2)), 3 => B(Has(compound, 2)), 1 => 1 + 2 * B(Has(compound, 2)),
                _ => throw new InvalidDataException("Invalid VP9 single reference.")
            };
        }

        private static int LastNeighbor(Vp9BlockInfo neighbor)
        {
            if (!neighbor.Inter) { return 2; }
            return neighbor.ReferenceOne == 0 ? 4 * B(neighbor.ReferenceZero == 1) : 1 + B(Has(neighbor, 1));
        }
        private static bool Has(Vp9BlockInfo block, int reference) => block.ReferenceZero == reference || block.ReferenceOne == reference;
        private static int B(bool value) => value ? 1 : 0;
    }
}

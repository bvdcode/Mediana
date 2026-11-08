// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal static class Av1ReferenceOrdering
    {
        internal static Av1ReferenceIndices Resolve(ReadOnlySpan<int> orderHints, int currentHint, int bits, int last, int golden)
        {
            if (orderHints.Length != Av1ReferencePictures.Count || bits is < 1 or > 8 ||
                last is < 0 or >= Av1ReferencePictures.Count || golden is < 0 or >= Av1ReferencePictures.Count)
            {
                throw new ArgumentException("The AV1 short-reference configuration is invalid.");
            }
            Av1ReferenceIndices references = default;
            for (int index = 0; index < Av1ReferenceIndices.Count; index++) { references[index] = -1; }
            references[0] = last;
            references[3] = golden;
            int used = (1 << last) | (1 << golden);
            int current = 1 << (bits - 1);
            Span<int> shifted = stackalloc int[Av1ReferencePictures.Count];
            for (int index = 0; index < shifted.Length; index++) { shifted[index] = current + Distance(orderHints[index], currentHint, bits); }
            if (shifted[last] >= current || shifted[golden] >= current)
            {
                throw new InvalidDataException("AV1 short-signaled last and golden references must precede the current picture.");
            }
            int selected = Find(shifted, current, used, true, true);
            if (selected >= 0)
            {
                references[6] = selected;
                used |= 1 << selected;
            }
            for (int index = 4; index < 6; index++)
            {
                selected = Find(shifted, current, used, true, false);
                if (selected >= 0)
                {
                    references[index] = selected;
                    used |= 1 << selected;
                }
            }
            ReadOnlySpan<byte> remaining = [1, 2, 4, 5, 6];
            foreach (int index in remaining)
            {
                if (references[index] >= 0) { continue; }
                selected = Find(shifted, current, used, false, true);
                if (selected >= 0)
                {
                    references[index] = selected;
                    used |= 1 << selected;
                }
            }
            int earliest = 0;
            for (int index = 1; index < shifted.Length; index++)
            {
                if (shifted[index] < shifted[earliest]) { earliest = index; }
            }
            for (int index = 0; index < Av1ReferenceIndices.Count; index++)
            {
                if (references[index] < 0) { references[index] = earliest; }
            }
            return references;
        }

        internal static bool HasSkipPair(Av1ReferenceIndices indices, Av1ReferencePictures references, int current, int bits)
            => TryGetSkipPair(indices, references, current, bits, out _);

        internal static bool TryGetSkipPair(Av1ReferenceIndices indices, Av1ReferencePictures references, int current, int bits, out Av1ReferencePair pair)
        {
            pair = default;
            int forward = -1;
            int forwardHint = 0;
            int backward = -1;
            int backwardHint = 0;
            for (int index = 0; index < Av1ReferenceIndices.Count; index++)
            {
                int hint = references.OrderHint(indices[index]);
                int distance = Distance(hint, current, bits);
                if (distance < 0 && (forward < 0 || Distance(hint, forwardHint, bits) > 0))
                {
                    forward = index;
                    forwardHint = hint;
                }
                if (distance > 0 && (backward < 0 || Distance(hint, backwardHint, bits) < 0))
                {
                    backward = index;
                    backwardHint = hint;
                }
            }
            if (forward < 0) { return false; }
            if (backward < 0)
            {
                for (int index = 0; index < Av1ReferenceIndices.Count; index++)
                {
                    int hint = references.OrderHint(indices[index]);
                    if (Distance(hint, forwardHint, bits) < 0 && (backward < 0 || Distance(hint, backwardHint, bits) > 0))
                    {
                        backward = index;
                        backwardHint = hint;
                    }
                }
            }
            if (backward < 0) { return false; }
            pair = new(Math.Min(forward, backward) + 1, Math.Max(forward, backward) + 1);
            return true;
        }

        internal static int Distance(int first, int second, int bits)
        {
            if (bits == 0) { return 0; }
            int sign = 1 << (bits - 1);
            int difference = first - second;
            return (difference & (sign - 1)) - (difference & sign);
        }

        private static int Find(ReadOnlySpan<int> hints, int current, int used, bool backward, bool latest)
        {
            int selected = -1;
            for (int index = 0; index < hints.Length; index++)
            {
                if ((used & (1 << index)) != 0 || (hints[index] >= current) != backward) { continue; }
                if (selected < 0 || (latest ? hints[index] >= hints[selected] : hints[index] < hints[selected])) { selected = index; }
            }
            return selected;
        }
    }
}

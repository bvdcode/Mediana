// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264ReferenceSyntax
    {
        public static IReadOnlyList<H264ReferenceModification> ReadModifications(ref H264BitReader bits, int activeCount, int maxFrameNumber)
        {
            if (!bits.ReadFlag())
            {
                return [];
            }
            List<H264ReferenceModification> result = [];
            while (true)
            {
                int operation = bits.ReadUe(3);
                if (operation == 3)
                {
                    return result.AsReadOnly();
                }
                if (result.Count == activeCount)
                {
                    throw new InvalidDataException("Too many H.264 reference list modifications for the active list.");
                }
                int value = operation switch
                {
                    0 or 1 => bits.ReadUe(maxFrameNumber - 1),
                    2 => bits.ReadUe(15),
                    _ => throw new InvalidDataException("Invalid H.264 reference list modification.")
                };
                result.Add(new H264ReferenceModification(operation, value));
            }
        }

        public static IReadOnlyList<H264MemoryOperation> ReadMarking(ref H264BitReader bits, int maxFrameNumber, int maximumOperations)
        {
            List<H264MemoryOperation> result = [];
            while (true)
            {
                int operation = bits.ReadUe(6);
                if (operation == 0)
                {
                    return result.AsReadOnly();
                }
                if (result.Count == maximumOperations)
                {
                    throw new NotSupportedException("H.264 reference marking exceeds the configured operation limit.");
                }
                int difference = operation is 1 or 3 ? bits.ReadUe(maxFrameNumber - 1) : 0;
                int longTermPicture = operation == 2 ? bits.ReadUe(15) : 0;
                int longTermFrame = operation is 3 or 6 ? bits.ReadUe(15) : 0;
                int maximumLongTerm = operation == 4 ? bits.ReadUe(16) : 0;
                result.Add(new H264MemoryOperation(operation, difference, longTermPicture, longTermFrame, maximumLongTerm));
            }
        }
    }
}

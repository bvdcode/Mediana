// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal partial class H264ReferenceBuffer
    {
        private static void ApplyMarking(List<H264ReferencePicture> next, H264SliceHeader header, H264PictureIdentity currentIdentity,
            ref int maximumLongTerm, ref int? currentLongTerm, ref int frameNumber)
        {
            IReadOnlyList<H264MemoryOperation> operations = header.MemoryOperations;
            if (operations.Any(value => value.Operation == 5) && operations.Any(value => value.Operation is 1 or 2 or 3))
            {
                throw new InvalidDataException("H.264 reference reset cannot be combined with operations 1 through 3.");
            }
            int seen = 0;
            H264ReferenceNumbering numbering = new(header);
            foreach (H264MemoryOperation operation in operations)
            {
                int flag = 1 << operation.Operation;
                if (operation.Operation is 4 or 5 or 6 && (seen & flag) != 0)
                {
                    throw new InvalidDataException("An H.264 reference marking operation occurs more than once.");
                }
                seen |= flag;
                int targetNumber = numbering.CurrentPictureNumber - (operation.DifferenceMinusOne + 1);
                switch (operation.Operation)
                {
                    case 1:
                        RemoveReference(next, FindShort(header.Layout.IsField ? next : GetFrames(next), targetNumber, numbering), header.Layout.IsField);
                        break;
                    case 2:
                        RemoveReference(next, FindLong(header.Layout.IsField ? next : GetFrames(next), operation.LongTermPictureNumber, numbering), header.Layout.IsField);
                        break;
                    case 3:
                        H264ReferencePicture selected = FindShort(header.Layout.IsField ? next : GetFrames(next), targetNumber, numbering);
                        ValidateLongIndex(operation.LongTermFrameIndex, maximumLongTerm,
                            selected.FrameIdentity == currentIdentity ? null : currentLongTerm);
                        ValidateFieldAssignment(next, selected, operation.LongTermFrameIndex, numbering, currentIdentity);
                        next.RemoveAll(value => value.LongTermIndex == operation.LongTermFrameIndex && value.FrameIdentity != selected.FrameIdentity);
                        AssignLongTerm(next, selected, operation.LongTermFrameIndex, header.Layout.IsField);
                        break;
                    case 4:
                        int newMaximum = operation.MaxLongTermFrameIndexPlusOne - 1;
                        if (operation.MaxLongTermFrameIndexPlusOne > header.Sequence.ReferenceFrames ||
                            (currentLongTerm is not null && currentLongTerm.Value > newMaximum))
                        {
                            throw new InvalidDataException("The H.264 long-term reference range is invalid.");
                        }
                        maximumLongTerm = newMaximum;
                        next.RemoveAll(value => value.LongTermIndex is not null && value.LongTermIndex.Value > newMaximum);
                        break;
                    case 5:
                        if (currentLongTerm is not null)
                        {
                            throw new InvalidDataException("H.264 reference reset cannot follow current-picture long-term assignment.");
                        }
                        next.Clear();
                        maximumLongTerm = -1;
                        frameNumber = 0;
                        break;
                    case 6:
                        ValidateLongIndex(operation.LongTermFrameIndex, maximumLongTerm, currentLongTerm);
                        currentLongTerm = operation.LongTermFrameIndex;
                        next.RemoveAll(value => value.LongTermIndex == operation.LongTermFrameIndex && value.FrameIdentity != currentIdentity);
                        break;
                    default:
                        throw new InvalidDataException("Invalid H.264 reference memory operation.");
                }
            }
        }

        private static void ValidateLongIndex(int index, int maximum, int? current)
        {
            if (index < 0 || index > maximum || current == index)
            {
                throw new InvalidDataException("The H.264 long-term reference index is unavailable.");
            }
        }

        private static void RemoveReference(List<H264ReferencePicture> next, H264ReferencePicture selected, bool field)
        {
            if (field)
            {
                next.Remove(selected);
            }
            else
            {
                next.RemoveAll(value => value.FrameIdentity == selected.FrameIdentity);
            }
        }

        private static void AssignLongTerm(List<H264ReferencePicture> next, H264ReferencePicture selected, int index, bool field)
        {
            for (int position = 0; position < next.Count; position++)
            {
                H264ReferencePicture reference = next[position];
                if (field ? reference == selected : reference.FrameIdentity == selected.FrameIdentity)
                {
                    next[position] = reference with { LongTermIndex = index };
                }
            }
        }

        private static void ValidateFieldAssignment(List<H264ReferencePicture> next, H264ReferencePicture selected,
            int longTermIndex, H264ReferenceNumbering numbering, H264PictureIdentity currentIdentity)
        {
            if (!numbering.Header.Layout.IsField)
            {
                return;
            }
            IReadOnlyList<H264MemoryOperation> operations = numbering.Header.MemoryOperations;
            if (selected.FrameIdentity == currentIdentity &&
                !operations.Any(operation => operation.Operation == 6 && operation.LongTermFrameIndex == longTermIndex))
            {
                throw new InvalidDataException("H.264 long-term assignment must include the complementary current field.");
            }
            foreach (H264ReferencePicture companion in next)
            {
                if (companion.FrameIdentity == selected.FrameIdentity && companion != selected && companion.LongTermIndex != longTermIndex)
                {
                    int difference = numbering.CurrentPictureNumber - numbering.ShortTerm(companion) - 1;
                    if (!operations.Any(operation => operation.Operation == 3 && operation.DifferenceMinusOne == difference &&
                        operation.LongTermFrameIndex == longTermIndex))
                    {
                        throw new InvalidDataException("H.264 long-term assignment must include both complementary reference fields.");
                    }
                }
            }
        }

        private static H264ReferencePicture FindShort(IEnumerable<H264ReferencePicture> list, int number, H264ReferenceNumbering numbering) =>
            list.FirstOrDefault(value => value.LongTermIndex is null && numbering.ShortTerm(value) == number)
            ?? throw new InvalidDataException("The H.264 short-term reference picture is missing.");

        private static H264ReferencePicture FindLong(IEnumerable<H264ReferencePicture> list, int number, H264ReferenceNumbering numbering) =>
            list.FirstOrDefault(value => value.LongTermIndex is not null && numbering.LongTerm(value) == number)
            ?? throw new InvalidDataException("The H.264 long-term reference picture is missing.");
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal partial class H264ReferenceBuffer : IDisposable
    {
        private List<H264ReferencePicture> references = [];
        private H264SequenceParameterSet? sequence;
        private int maximumLongTermIndex = -1;
        private bool disposed;

        public int Count => references.Count;

        /// <summary>Transfers picture ownership on successful return. Non-reference pictures are released.</summary>
        public void Store(H264Picture picture, H264SliceHeader header, H264PictureOrder order)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ValidateSequence(header);
            if (picture.Sequence != header.Sequence)
            {
                throw new InvalidDataException("The decoded H.264 picture does not match its sequence.");
            }
            if (header.ReferenceIndicator == 0)
            {
                picture.Dispose();
                return;
            }
            List<H264ReferencePicture> next = header.IsIdr ? [] : [.. references];
            int nextMaximum = header.IsIdr ? (header.LongTermReference ? 0 : -1) : maximumLongTermIndex;
            int? longTermIndex = header.IsIdr && header.LongTermReference ? 0 : null;
            int frameNumber = header.FrameNumber;
            int maximumFrames = Math.Max(header.Sequence.ReferenceFrames, 1);
            int maximumNumber = 1 << header.Sequence.FrameNumberBits;
            if (!header.IsIdr && header.AdaptiveMarking)
            {
                ApplyMarking(next, header, ref nextMaximum, ref longTermIndex, ref frameNumber);
            }
            else if (!header.IsIdr && next.Count == maximumFrames)
            {
                H264ReferencePicture? oldest = next.Where(value => value.LongTermIndex is null)
                    .MinBy(value => value.PictureNumber(header.FrameNumber, maximumNumber));
                if (oldest is null)
                {
                    throw new InvalidDataException("The H.264 sliding window has no short-term picture to release.");
                }
                next.Remove(oldest);
            }
            if (next.Count >= maximumFrames || (longTermIndex is null && next.Any(value => value.LongTermIndex is null && value.FrameNumber == frameNumber)))
            {
                throw new InvalidDataException("H.264 reference marking exceeds the reference limit or repeats a short-term frame number.");
            }
            next.Add(new H264ReferencePicture(picture, frameNumber, order, longTermIndex));
            foreach (H264ReferencePicture previous in references)
            {
                if (!next.Any(value => ReferenceEquals(value.Picture, previous.Picture)))
                {
                    previous.Picture.Dispose();
                }
            }
            references = next;
            maximumLongTermIndex = nextMaximum;
            sequence = header.Sequence;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            foreach (H264ReferencePicture reference in references)
            {
                reference.Picture.Dispose();
            }
            references.Clear();
            sequence = null;
            disposed = true;
        }

        private static void ApplyMarking(List<H264ReferencePicture> next, H264SliceHeader header,
            ref int maximumLongTerm, ref int? currentLongTerm, ref int frameNumber)
        {
            IReadOnlyList<H264MemoryOperation> operations = header.MemoryOperations;
            if (operations.Any(value => value.Operation == 5) && operations.Any(value => value.Operation is 1 or 2 or 3))
            {
                throw new InvalidDataException("H.264 reference reset cannot be combined with operations 1 through 3.");
            }
            int seen = 0;
            int maximumNumber = 1 << header.Sequence.FrameNumberBits;
            foreach (H264MemoryOperation operation in operations)
            {
                int flag = 1 << operation.Operation;
                if (operation.Operation is 4 or 5 or 6 && (seen & flag) != 0)
                {
                    throw new InvalidDataException("An H.264 reference marking operation occurs more than once.");
                }
                seen |= flag;
                int targetNumber = header.FrameNumber - (operation.DifferenceMinusOne + 1);
                switch (operation.Operation)
                {
                    case 1:
                        next.Remove(FindShort(next, targetNumber, header.FrameNumber, maximumNumber));
                        break;
                    case 2:
                        next.Remove(FindLong(next, operation.LongTermPictureNumber));
                        break;
                    case 3:
                        ValidateLongIndex(operation.LongTermFrameIndex, maximumLongTerm, currentLongTerm);
                        H264ReferencePicture selected = FindShort(next, targetNumber, header.FrameNumber, maximumNumber);
                        next.RemoveAll(value => value.LongTermIndex == operation.LongTermFrameIndex);
                        next.Remove(selected);
                        next.Add(selected with { LongTermIndex = operation.LongTermFrameIndex });
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
                        next.RemoveAll(value => value.LongTermIndex == operation.LongTermFrameIndex);
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

        private static H264ReferencePicture FindShort(IEnumerable<H264ReferencePicture> list, int number, int current, int maximum) =>
            list.FirstOrDefault(value => value.LongTermIndex is null && value.PictureNumber(current, maximum) == number)
            ?? throw new InvalidDataException("The H.264 short-term reference picture is missing.");

        private static H264ReferencePicture FindLong(IEnumerable<H264ReferencePicture> list, int number) =>
            list.FirstOrDefault(value => value.LongTermIndex == number)
            ?? throw new InvalidDataException("The H.264 long-term reference picture is missing.");

        private void ValidateSequence(H264SliceHeader header)
        {
            if (!header.IsIdr && sequence != header.Sequence)
            {
                throw new NotSupportedException("A new H.264 sequence must begin with an IDR picture.");
            }
        }
    }
}

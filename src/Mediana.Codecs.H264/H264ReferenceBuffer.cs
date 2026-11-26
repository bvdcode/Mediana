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
        private H264ReferenceField? previousField;

        public int Count => references.Count;

        /// <summary>Transfers picture ownership on successful return. Non-reference pictures are released.</summary>
        public void Store(H264Picture picture, H264SliceHeader header, H264PictureOrder order)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ValidateSequence(header);
            if (picture.Sequence != header.Sequence || picture.Structure != header.Structure || order.Structure != header.Structure)
            {
                throw new InvalidDataException("The decoded H.264 picture does not match its sequence.");
            }
            if (header.ReferenceIndicator == 0)
            {
                picture.Dispose();
                previousField = null;
                return;
            }
            H264ReferenceField? complementary = previousField is H264ReferenceField previousReference && previousReference.Complements(header)
                ? previousReference : null;
            H264PictureIdentity frameIdentity = complementary?.FrameIdentity ?? picture.Identity;
            List<H264ReferencePicture> next = header.IsIdr ? [] : [.. references];
            if (header.Layout.IsField)
            {
                SeparateFields(next);
            }
            int nextMaximum = header.IsIdr ? (header.LongTermReference ? 0 : -1) : maximumLongTermIndex;
            int? longTermIndex = header.IsIdr && header.LongTermReference ? 0 : null;
            int frameNumber = header.FrameNumber;
            if (!header.IsIdr && header.AdaptiveMarking)
            {
                ApplyMarking(next, header, frameIdentity, ref nextMaximum, ref longTermIndex, ref frameNumber);
            }
            else if (!header.IsIdr)
            {
                ApplySlidingWindow(next, header, frameIdentity);
            }
            if (complementary is { LongTermIndex: int previousLongTerm } && longTermIndex != previousLongTerm)
            {
                throw new InvalidDataException("Complementary H.264 reference fields must use the same long-term index.");
            }
            next.Add(new H264ReferencePicture(picture, frameNumber, order, longTermIndex) { FrameIdentity = frameIdentity });
            ValidateReferences(next, header);
            ReleaseUnusedPictures(next);
            references = next;
            maximumLongTermIndex = nextMaximum;
            sequence = header.Sequence;
            previousField = header.Layout.IsField && complementary is null
                ? new H264ReferenceField(frameIdentity, frameNumber, header.Structure, longTermIndex) : null;
        }

        public void DiscardNonReferenceFrame(H264SliceHeader header)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ValidateSequence(header);
            if (header.Layout.IsField || header.ReferenceIndicator != 0)
            {
                throw new InvalidOperationException("Discarding H.264 preroll requires a complete non-reference frame.");
            }
            previousField = null;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            ReleaseUnusedPictures([]);
            references.Clear();
            sequence = null;
            previousField = null;
            disposed = true;
        }

        private static void ApplySlidingWindow(List<H264ReferencePicture> next, H264SliceHeader header, H264PictureIdentity currentIdentity)
        {
            if (next.Any(value => value.FrameIdentity == currentIdentity && value.LongTermIndex is null))
            {
                return;
            }
            if (CountFrames(next) < Math.Max(header.Sequence.ReferenceFrames, 1))
            {
                return;
            }
            int maximumNumber = 1 << header.Sequence.FrameNumberBits;
            H264ReferencePicture? oldest = next.Where(value => value.LongTermIndex is null)
                .MinBy(value => value.PictureNumber(header.FrameNumber, maximumNumber));
            if (oldest is null)
            {
                throw new InvalidDataException("The H.264 sliding window has no short-term picture to release.");
            }
            next.RemoveAll(value => value.FrameIdentity == oldest.FrameIdentity);
        }

        private static void ValidateReferences(List<H264ReferencePicture> next, H264SliceHeader header)
        {
            if (CountFrames(next) > Math.Max(header.Sequence.ReferenceFrames, 1))
            {
                throw new InvalidDataException("H.264 reference marking exceeds the reference frame limit.");
            }
            for (int index = 0; index < next.Count; index++)
            {
                H264ReferencePicture current = next[index];
                for (int previous = 0; previous < index; previous++)
                {
                    H264ReferencePicture other = next[previous];
                    if (current.FrameIdentity == other.FrameIdentity)
                    {
                        if (current.Structure == H264PictureStructure.Frame || other.Structure == H264PictureStructure.Frame ||
                            current.Structure == other.Structure || current.LongTermIndex != other.LongTermIndex)
                        {
                            throw new InvalidDataException("Invalid complementary H.264 reference field marking.");
                        }
                    }
                    else if (current.LongTermIndex is null && other.LongTermIndex is null && current.FrameNumber == other.FrameNumber)
                    {
                        throw new InvalidDataException("H.264 reference marking repeats a short-term frame number.");
                    }
                }
            }
        }

        private static int CountFrames(List<H264ReferencePicture> list)
        {
            int count = 0;
            for (int index = 0; index < list.Count; index++)
            {
                bool first = true;
                for (int previous = 0; previous < index; previous++)
                {
                    if (list[index].FrameIdentity == list[previous].FrameIdentity)
                    {
                        first = false;
                        break;
                    }
                }
                if (first)
                {
                    count++;
                }
            }
            return count;
        }

        private void ValidateSequence(H264SliceHeader header)
        {
            if (!header.IsIdr && sequence != header.Sequence)
            {
                throw new NotSupportedException("A new H.264 sequence must begin with an IDR picture.");
            }
        }
    }
}

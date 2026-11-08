// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Av1
{
    internal class Av1ReferencePictures : IDisposable
    {
        internal const int Count = 8;
        internal const int All = (1 << Count) - 1;
        private readonly Av1StoredPicture?[] pictures = new Av1StoredPicture?[Count];

        internal int OrderHint(int index) => pictures[index]?.Header.OrderHint ?? 0;

        internal Av1StoredPicture Get(int index) => pictures[index] ?? throw new InvalidDataException("The AV1 reference picture has not been initialized.");

        internal int Expired(int frameId, int idBits, int deltaBits)
        {
            if (idBits == 0) { return 0; }
            int distance = 1 << deltaBits;
            int modulus = 1 << idBits;
            int invalid = 0;
            for (int index = 0; index < Count; index++)
            {
                Av1StoredPicture? picture = pictures[index];
                if (picture is null) { continue; }
                int previous = picture.Header.FrameId;
                bool expired = frameId > distance
                    ? previous > frameId || previous < frameId - distance
                    : previous > frameId && previous < modulus + frameId - distance;
                if (expired) { invalid |= 1 << index; }
            }
            return invalid;
        }

        internal void Save(Av1Picture picture, ReadOnlySpan<ushort> probabilities, MediaBufferAllocator allocator, MediaBuffer? motionField = null)
        {
            if (picture.Header.RefreshReferences == 0) { return; }
            if ((motionField?.Memory.Length ?? 0) != Av1MotionField.StoredLength(picture.Header.Geometry, picture.Header))
            {
                throw new ArgumentException("The AV1 reference motion-field extent is invalid.", nameof(motionField));
            }
            using WritableMediaBuffer write = allocator.Allocate(checked(probabilities.Length * sizeof(ushort)));
            MemoryMarshal.AsBytes(probabilities).CopyTo(write.Span);
            using MediaBuffer cdfs = write.Publish();
            int alternateOrderHint = 0;
            if (picture.Header.FrameType is Av1FrameType.Inter or Av1FrameType.Switch)
            {
                alternateOrderHint = OrderHint(picture.Header.Inter.References[6]);
            }
            Av1StoredPicture stored = new(picture, cdfs, motionField, alternateOrderHint);
            Refresh(stored, picture.Header.RefreshReferences);
        }

        internal Av1Picture Show(int index)
        {
            Av1StoredPicture stored = Get(index);
            if (!stored.Showable)
            {
                throw new InvalidDataException("The AV1 reference picture is not showable.");
            }
            if (stored.Header.FrameType == Av1FrameType.Key)
            {
                stored.Showable = false;
                Refresh(stored, All);
            }
            Av1FrameHeader header = stored.Header with { ShowFrame = true, TileOffset = 0 };
            return new(header, stored.Pixels.Retain());
        }

        internal void Invalidate(int flags)
        {
            for (int index = 0; index < pictures.Length; index++)
            {
                if ((flags & (1 << index)) == 0) { continue; }
                Av1StoredPicture? old = pictures[index];
                pictures[index] = null;
                if (old is not null && !Contains(old)) { old.Dispose(); }
            }
        }

        private void Refresh(Av1StoredPicture current, int flags)
        {
            for (int index = 0; index < pictures.Length; index++)
            {
                if ((flags & (1 << index)) == 0) { continue; }
                Av1StoredPicture? old = pictures[index];
                pictures[index] = current;
                if (old is not null && !Contains(old)) { old.Dispose(); }
            }
        }

        private bool Contains(Av1StoredPicture picture)
        {
            foreach (Av1StoredPicture? current in pictures)
            {
                if (ReferenceEquals(current, picture)) { return true; }
            }
            return false;
        }

        public void Dispose() => Invalidate(All);
    }
}

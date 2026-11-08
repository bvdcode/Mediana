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

        internal void Save(Av1Picture picture, ReadOnlySpan<ushort> probabilities, MediaBufferAllocator allocator)
        {
            if (picture.Header.RefreshReferences == 0) { return; }
            using WritableMediaBuffer write = allocator.Allocate(checked(probabilities.Length * sizeof(ushort)));
            MemoryMarshal.AsBytes(probabilities).CopyTo(write.Span);
            using MediaBuffer cdfs = write.Publish();
            Av1StoredPicture stored = new(picture, cdfs);
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

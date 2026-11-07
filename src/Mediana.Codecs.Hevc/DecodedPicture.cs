// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Hevc
{
    internal class DecodedPicture : IDisposable
    {
        private WritableMediaBuffer? writablePixels;
        private MediaBuffer? publishedPixels;
        private bool disposed;

        public DecodedPicture(SequenceParameters sequence)
        {
            Sequence = sequence;
            Luma = new SamplePlane(sequence.Width, sequence.Height, sequence.BitDepth);
            bool initialized = false;
            try
            {
                if (sequence.ChromaFormat != 0)
                {
                    Cb = new SamplePlane(sequence.Width / sequence.SubWidthC, sequence.Height / sequence.SubHeightC, sequence.BitDepth);
                    Cr = new SamplePlane(sequence.Width / sequence.SubWidthC, sequence.Height / sequence.SubHeightC, sequence.BitDepth);
                }

                initialized = true;
            }
            finally
            {
                if (!initialized)
                {
                    Dispose();
                }
            }
        }

        /// <summary>Allocates one budgeted pixel buffer shared by reconstruction, references and published frames.</summary>
        public DecodedPicture(SequenceParameters sequence, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(sequence); ArgumentNullException.ThrowIfNull(allocator);
            Sequence = sequence;
            int bytesPerSample = (sequence.BitDepth + 7) / 8;
            int lumaBytes = checked(sequence.Width * sequence.Height * bytesPerSample);
            int chromaBytes = sequence.ChromaFormat == 0 ? 0 : checked((sequence.Width / sequence.SubWidthC) * (sequence.Height / sequence.SubHeightC) * bytesPerSample);
            bool initialized = false;
            try
            {
                writablePixels = allocator.Allocate(checked(lumaBytes + 2 * chromaBytes));
                Luma = new(sequence.Width, sequence.Height, sequence.BitDepth, writablePixels.Memory[..lumaBytes]);
                if (sequence.ChromaFormat != 0)
                {
                    Cb = new(sequence.Width / sequence.SubWidthC, sequence.Height / sequence.SubHeightC,
                        sequence.BitDepth, writablePixels.Memory.Slice(lumaBytes, chromaBytes));
                    Cr = new(sequence.Width / sequence.SubWidthC, sequence.Height / sequence.SubHeightC,
                        sequence.BitDepth, writablePixels.Memory.Slice(lumaBytes + chromaBytes, chromaBytes));
                }
                initialized = true;
            }
            finally { if (!initialized) { Dispose(); } }
        }

        public MediaBuffer PublishPixels()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (publishedPixels is null)
            {
                if (writablePixels is null) { throw new InvalidOperationException("HEVC pixel publication requires picture-owned buffer storage."); }
                Luma.Freeze(); Cb?.Freeze(); Cr?.Freeze();
                publishedPixels = writablePixels.Publish();
                writablePixels = null;
            }
            return publishedPixels.Retain();
        }

        public SequenceParameters Sequence { get; }
        public SamplePlane Luma { get; } = null!;
        public SamplePlane? Cb { get; }
        public SamplePlane? Cr { get; }

        public void Dispose()
        {
            if (disposed) { return; }
            Luma?.Dispose();
            Cb?.Dispose();
            Cr?.Dispose();
            writablePixels?.Dispose(); writablePixels = null;
            publishedPixels?.Dispose(); publishedPixels = null;
            disposed = true;
        }
    }
}

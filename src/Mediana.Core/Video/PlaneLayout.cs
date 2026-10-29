// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Core.Video
{
    /// <summary>A positive-stride plane within a shared buffer.</summary>
    public readonly record struct PlaneLayout(int Offset, int Stride, int RowBytes, int Height)
    {
        public long EndOffset => (long)Offset + (long)Stride * (Height - 1) + RowBytes;

        public void Validate(int bufferLength)
        {
            if (Offset < 0 || RowBytes <= 0 || Height <= 0 || Stride < RowBytes || EndOffset > bufferLength)
            {
                throw new ArgumentException("The plane lies outside its buffer or has an invalid layout.");
            }
        }
    }
}

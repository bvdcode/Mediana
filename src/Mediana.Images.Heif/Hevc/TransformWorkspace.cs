// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Buffers;

namespace Mediana.Images.Heif.Hevc
{
    /// <summary>
    /// Owns temporary transform storage for one decoding worker.
    /// </summary>
    /// <remarks>
    /// Only one transform may use a workspace at a time. Its spans must not escape
    /// the worker or remain in use after disposal. Reused samples are overwritten before reading.
    /// </remarks>
    internal class TransformWorkspace : IDisposable
    {
        private const int MaximumBlockSamples = 32 * 32;
        private readonly int[] intermediate = ArrayPool<int>.Shared.Rent(MaximumBlockSamples);
        private bool disposed;

        public Span<int> Intermediate
        {
            get
            {
                if (disposed)
                {
                    throw new ObjectDisposedException(nameof(TransformWorkspace), "Transform storage has been returned to its pool.");
                }
                return intermediate.AsSpan(0, MaximumBlockSamples);
            }
        }

        public void Dispose()
        {
            if (!disposed)
            {
                disposed = true;
                ArrayPool<int>.Shared.Return(intermediate);
            }
        }
    }
}

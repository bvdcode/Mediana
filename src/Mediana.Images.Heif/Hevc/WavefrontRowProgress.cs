// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mediana.Images.Heif.Hevc
{
    /// <summary>
    /// Publishes completed CTUs from one row to the single row directly below it.
    /// </summary>
    /// <remarks>
    /// Each instance has one producer and one consumer. Samples, coding metadata and
    /// CABAC snapshots precede the progress write. Notifications can coalesce; the
    /// counter determines readiness. Disposal follows completion of every worker.
    /// </remarks>
    internal class WavefrontRowProgress : IDisposable
    {
        private readonly SemaphoreSlim advanced = new SemaphoreSlim(0, 1);
        private int completedColumns;

        public async Task WaitForAsync(int columns, CancellationToken cancellation)
        {
            while (Volatile.Read(ref completedColumns) < columns)
            {
                await advanced.WaitAsync(cancellation).ConfigureAwait(false);
            }
        }

        public void Advance(int columns)
        {
            Volatile.Write(ref completedColumns, columns);
            if (advanced.CurrentCount == 0)
            {
                advanced.Release();
            }
        }

        public void Dispose() => advanced.Dispose();
    }
}

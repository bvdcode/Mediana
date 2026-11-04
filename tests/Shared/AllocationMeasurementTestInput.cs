// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime;

namespace Mediana.Tests
{
    internal static class AllocationMeasurementTestInput
    {
        public static void Verify()
        {
            if (GCSettings.LatencyMode != GCLatencyMode.Batch)
            {
                throw new InvalidOperationException("Exact allocation checks require non-concurrent GC in the test host.");
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            byte[] allocated = new byte[37];
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(allocated);
            if (bytes <= 0)
            {
                throw new InvalidOperationException("The allocation counter did not detect the positive control.");
            }
        }
    }
}

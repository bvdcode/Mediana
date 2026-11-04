// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.NativeTests
{
    internal static class NativeCoreMemoryTests
    {
        public static void Run()
        {
            WritableMediaSpanTestInput.VerifyPublication();
            WritableMediaSpanTestInput.VerifyDisposal();
            WritableMediaSpanTestInput.VerifyAllocation();
            Console.WriteLine("PASS writable media spans: shared storage, publication, retained leases, disposal and allocation checks.");
        }
    }
}

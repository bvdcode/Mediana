// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.NativeTests
{
    internal static class NativeMeshTests
    {
        internal static async Task RunAsync()
        {
            await MeshTestInput.VerifyAsync();
            Console.WriteLine("PASS mesh: STL/OBJ/3MF geometry, OPC/Word routing, bounded rendering, frame publication, PNG, concurrency and cancellation.");
        }
    }
}

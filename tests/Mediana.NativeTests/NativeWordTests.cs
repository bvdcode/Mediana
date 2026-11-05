// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.NativeTests
{
    internal static class NativeWordTests
    {
        internal static async Task RunAsync()
        {
            OpenXmlPackageTestInput.Verify();
            WordTestInput.Verify();
            await WordRenderTestInput.VerifyAsync();
            await FluentEmojiTestInput.VerifyAsync();
            await FluentEmojiTestInput.VerifySharedTextAsync();
            Console.WriteLine("PASS Word: public pagination, page rasterization, font ownership and Open XML registration.");
        }
    }
}

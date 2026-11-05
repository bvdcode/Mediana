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
            await DocumentResourceTestInput.VerifyAsync();
            await EmbeddedImageTestInput.VerifyRegistryAsync();
            await EmbeddedImageTestInput.VerifyColorAsync();
            await EmbeddedImageTestInput.VerifyJpegAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Jpeg"));
            await FluentEmojiTestInput.VerifyAsync();
            await FluentEmojiTestInput.VerifySharedTextAsync();
            Console.WriteLine("PASS Word: public pagination, page rasterization, font ownership and Open XML registration.");
        }
    }
}

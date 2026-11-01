// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.NativeTests
{
    internal static class NativeServerPreviewTests
    {
        internal static async Task RunAsync()
        {
            await ContainerDecodeTransferTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data"));
            Console.WriteLine("PASS container transfer: complete MP4/MKV video pixels, restarted seek, MP4/MKV/MP3 audio and original storage ownership.");
            await ServerContainerPreviewTestInput.VerifyRealCoversAsync(Path.Combine(AppContext.BaseDirectory, "Data"));
            await ServerContainerPreviewTestInput.VerifySelectionAsync(Path.Combine(AppContext.BaseDirectory, "Data"));
            await ServerContainerPreviewTestInput.VerifyRecognizedFailureAsync(Path.Combine(AppContext.BaseDirectory, "Data"));
            ServerContainerPreviewTestInput.VerifyTargets();
            Console.WriteLine("PASS container previews: original cover priority, complete PNG/Exif pixels, recognized failures and declared target policy.");
            foreach (string name in new[] { "frame-rate-cfr.mp4", "frame-rate-vfr.mp4", "frame-rate-edited.mp4", "preview-hd.mp4" })
            {
                await ServerVideoPreviewTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "FrameRate"), name);
            }
            Console.WriteLine("PASS server video previews: midpoint targets, VFR, movie edits, exact pixels, out-of-order requests and EOF restart.");
            await ServerVideoPreviewTestInput.VerifyPngAsync(Path.Combine(AppContext.BaseDirectory, "Data", "FrameRate"));
            Console.WriteLine("PASS server HD preview: complete sample PNG, dimensions, sRGB labels and every independent pixel.");
        }
    }
}

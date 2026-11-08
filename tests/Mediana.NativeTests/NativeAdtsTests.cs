// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.NativeTests
{
    internal static class NativeAdtsTests
    {
        public static async Task RunAsync()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data", "Aac");
            foreach (bool indexed in new[] { false, true })
            {
                await AdtsDecodeTestInput.VerifyAsync(root, "lc-mono-48", 48000, 1, indexed);
                await AdtsDecodeTestInput.VerifyAsync(root, "lc-stereo-44", 44100, 2, indexed);
                foreach ((string name, int channels) in new[] { ("silence-1", 1), ("silence-2", 2), ("tone-1", 1), ("tone-2", 2), ("data-element", 1) })
                {
                    await AdtsDecodeTestInput.VerifyProtectedAsync(root, name, channels, indexed);
                }
                await AdtsMultiBlockTestInput.VerifyAsync(root, false, indexed);
                await AdtsMultiBlockTestInput.VerifyAsync(root, true, indexed);
            }
            await AdtsMultiBlockTestInput.VerifyEnhancementAsync(Path.Combine(AppContext.BaseDirectory, "Data", "AacSbr"), "transients-mono", 1);
            await AdtsMultiBlockTestInput.VerifyEnhancementAsync(Path.Combine(AppContext.BaseDirectory, "Data", "AacSbr"), "transients-stereo", 2);
            await AdtsMultiBlockTestInput.VerifyEnhancementAsync(Path.Combine(AppContext.BaseDirectory, "Data", "AacPs"), "transients", 2);
            await AdtsTransportCopyTestInput.VerifyAsync(root, "multi-protected");
            await AdtsTransportCopyTestInput.VerifyAsync(root, "multi-unprotected");
            Console.WriteLine("PASS ADTS: independent mono/stereo PCM, exact timing, sequential and indexed reading, repeated seeks and released storage.");
            Console.WriteLine("PASS protected ADTS: complete transport CRC regions, overlapping stereo regions, DSE, corruption rejection and zero-copy retained side data.");
            Console.WriteLine("PASS multi-block ADTS: variable transport framing, exact LC/SBR/PS output, indexed seeks, atomic CRC validation and stream copy.");
        }
    }
}

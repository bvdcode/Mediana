// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.NativeTests
{
    internal static class NativeAc3Tests
    {
        public static async Task RunAsync()
        {
            await Ac3ContainerTestInput.VerifyAsync("mono.mp4", "mono.ac3", false, 1);
            await Ac3ContainerTestInput.VerifyAsync("stereo-eac3.mp4", "stereo.eac3", true, 2);
            await Ac3ContainerTestInput.VerifyBundledAndSeekAsync();
            await Ac3ContainerTestInput.VerifyTransientAsync();
            foreach (int channels in new[] { 8, 16 })
            {
                await Ac3ContainerTestInput.VerifyCompositionAsync(channels);
            }
            string root = Path.Combine(AppContext.BaseDirectory, "Data", "Ac3");
            foreach (string name in new[] { "spectral", "spectral-atten0", "spectral-atten31", "spectral-merged",
                "adaptive", "adaptive-switched", "adaptive-quantizers", "enhanced-coupling", "enhanced-coupling-merged", "enhanced-coupling-spectral" })
            {
                int channels = name.StartsWith("enhanced-coupling", StringComparison.Ordinal) ? 2 : 1;
                await Ac3PcmTestInput.VerifyAsync(root, name + ".eac3", name + ".pcm", 48000, channels, 100);
            }
            await Ac3PcmTestInput.VerifyAsync(root, "transient.eac3", "transient.pcm", 48000, 1, 135);
            foreach (int channels in new[] { 8, 16 })
            {
                await Ac3PcmTestInput.VerifyAsync(root, $"dependent-{channels}.eac3", $"dependent-{channels}.pcm", 48000, channels, 135);
            }
            await Console.Out.WriteLineAsync("PASS AC-3/E-AC-3: raw and MP4 packets, metadata, complete PCM, bundled syncframes, seek overlap and owned storage.");
            await Console.Out.WriteLineAsync("PASS E-AC-3: spectral extension, adaptive transforms, enhanced coupling and transient processing against complete independent PCM references.");
            await Console.Out.WriteLineAsync("PASS E-AC-3: dependent replacement and extension, raw/MP4 seeking and independent 8/16-channel PCM references.");
        }
    }
}

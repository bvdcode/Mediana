// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.NativeTests
{
    internal static class NativeIndexedMediaTests
    {
        public static async Task VerifyAsync(string root)
        {
            await FragmentHlsTestInput.VerifyAsync(root);
            await OnDemandHlsTestInput.VerifyAsync(root);
            await OggContainerTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Ogg"));
            if (OpusRangeTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data", "Opus")) != 65071)
            {
                throw new InvalidDataException("The Opus entropy reference operation count differs.");
            }
            Console.WriteLine("PASS Opus entropy: 65,071 independent symbols, final ranges and whole/fractional bit budgets match.");
            string opusRoot = Path.Combine(AppContext.BaseDirectory, "Data", "Opus");
            if (OpusPulseTestInput.Verify(opusRoot) != 13574 || OpusLaplaceTestInput.Verify(opusRoot) != 5257 ||
                OpusEnergyTestInput.Verify(opusRoot) != 768 || OpusAllocationTestInput.Verify(opusRoot) != 4096 ||
                OpusFrameSyntaxTestInput.Verify(opusRoot) != 1024 || OpusVectorTestInput.Verify(opusRoot) != 3793 ||
                OpusVectorTestInput.Verify(opusRoot, "normalized-vectors-16.bin") != 1864 ||
                OpusSplitTestInput.Verify(opusRoot) != 8105 || OpusSpectralTestInput.VerifyPartitions(opusRoot) != 7682 ||
                OpusSpectralTestInput.VerifyBands(opusRoot) != 7782 || OpusSpectralTestInput.VerifyStereoBands(opusRoot) != 7708 ||
                OpusSpectrumTestInput.Verify(opusRoot) != 1963 || OpusSpectrumTestInput.Verify(opusRoot, "encoded-spectra.bin") != 1152 ||
                OpusImdctTestInput.Verify(opusRoot) != 128 || OpusRestorationTestInput.Verify(opusRoot) != 512 ||
                OpusConfigurationTestInput.Verify(opusRoot) != 256 || OpusSynthesisTestInput.Verify(opusRoot) != 128 ||
                OpusCeltPcmTestInput.Verify(opusRoot) != 2448 || OpusSilkPulseTestInput.Verify(opusRoot) != 2880 ||
                OpusSilkControlsTestInput.Verify(opusRoot) != 1536 || OpusSilkNlsfTestInput.Verify(opusRoot) != 5888 ||
                OpusSilkLpcTestInput.Verify(opusRoot) != 6144 || OpusSilkIndicesTestInput.Verify(opusRoot) != 3456 ||
                OpusSilkParametersTestInput.Verify(opusRoot) != 2304 || OpusSilkFixedTestInput.Verify(opusRoot) != 12288 ||
                OpusSilkSynthesisTestInput.Verify(opusRoot) != 768 || OpusSilkStereoTestInput.Verify(opusRoot) != 1536 ||
                OpusSilkResamplerTestInput.Verify(opusRoot) != 768 || OpusSilkPcmTestInput.Verify(opusRoot) != 3072)
            {
                throw new InvalidDataException("The Opus reference case count differs.");
            }
            Console.WriteLine("PASS CELT: spectral stages, transforms, filtering and 2,448 sequential PCM frames match independent references.");
            Console.WriteLine("PASS SILK synthesis: frame controls, pulses, LSF/LPC, pitch rewhitening, PCM and retained histories match independent references.");
            Console.WriteLine("PASS on-demand HLS encoding: independent reverse and repeated mid-GOP windows, exact reference pixels, clocks and allocation release.");
            Console.WriteLine("PASS Ogg: independent Opus/Vorbis packets, metadata, reverse requests, seek preroll and borrowed source ownership.");
            Console.WriteLine("PASS indexed fMP4 HLS: initialization, reverse segment requests, repeated hashes and allocation release match.");
        }
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;
using Mediana.Tests;

namespace Mediana.NativeTests
{
    internal static class NativeAudioDownmixTests
    {
        internal static void Run()
        {
            foreach (bool stereo in new[] { false, true })
            {
                foreach (AudioStorageLayout layout in new[] { AudioStorageLayout.Interleaved, AudioStorageLayout.Planar })
                {
                    AudioDownmixTestInput.Verify(stereo, layout);
                    AudioDownmixTestInput.Verify(stereo, layout, AudioSampleFormat.Float32);
                    AudioDownmixTestInput.Verify(stereo, layout, AudioSampleFormat.Float32, sideSurround: true);
                }
            }
            AudioDownmixTestInput.VerifyBalance();
            Console.WriteLine("PASS explicit audio downmix: shuffled speakers, mono/stereo coefficients, correlated peaks, timing and released storage.");
        }
    }
}

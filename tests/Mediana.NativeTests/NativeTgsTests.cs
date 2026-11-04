// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.NativeTests
{
    internal static class NativeTgsTests
    {
        internal static async Task RunAsync()
        {
            await TgsReferenceTestInput.VerifyAllAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Tgs"));
            TgsStrokeTestInput.VerifyZeroPatterns();
            TgsStrokeTestInput.VerifyAnimatedZeroDash();
            TgsKeyframeOrderTestInput.Verify();
            TgsStoredValuesTestInput.Verify();
            TgsEffectTestInput.VerifyParameterGroups();
            TgsEffectTestInput.VerifyInvert();
            TgsLayerReferenceTestInput.VerifyUnreferencedDuplicates();
            TgsLayerReferenceTestInput.VerifyAmbiguousReferences();
            Console.WriteLine("PASS Lottie input: stable keyframe ordering, exact stroke endpoints, parameter groups, RGB inversion and layer references.");
            Console.WriteLine("PASS TGS/Lottie: 33 supported frames match every reference channel within one RGB code and exact alpha.");
        }
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.NativeTests
{
    internal static class NativeEbmlTests
    {
        public static async Task RunAsync()
        {
            await EbmlElementTestInput.VerifyBoundsAsync();
            await EbmlElementTestInput.VerifyTruncationAsync();
            await EbmlElementTestInput.VerifyFailureAndCancellationAsync();
            EbmlValueTestInput.VerifyVariableIntegers();
            EbmlValueTestInput.VerifyIdentifiers();
            EbmlValueTestInput.VerifyElementValues();
            EbmlValueTestInput.VerifyStrings();
            EbmlValueTestInput.VerifyInvalidEncodings();
            Console.WriteLine("PASS EBML: variable integers, defaults, text, bounded headers, cancellation and borrowed sources.");
        }
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    internal static class Av1SuperResolutionTestInput
    {
        internal static void VerifyPictures(string root)
        {
            for (int denominator = 9; denominator <= 16; denominator++)
            {
                Av1PictureSequenceTestInput.Verify(root, $"superres{denominator}", [0x81, 0, 13, 0]);
            }
            foreach (string suffix in new[] { "Odd", "Tiles", "Random", "Grain" })
            {
                Av1PictureSequenceTestInput.Verify(root, "superres" + suffix, [0x81, 0, 13, 0]);
            }
            Av1PictureSequenceTestInput.Verify(root, "superresMono", [0x81, 0, 28, 0]);
        }

        internal static void VerifyOwnership(string root)
        {
            foreach (string suffix in new[] { "Random", "Grain" })
            {
                Av1PictureSequenceTestInput.VerifyOwnership(root, "superres" + suffix, [0x81, 0, 13, 0]);
            }
        }
    }
}

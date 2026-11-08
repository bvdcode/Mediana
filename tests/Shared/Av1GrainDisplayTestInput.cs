// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    internal static class Av1GrainDisplayTestInput
    {
        internal static void VerifyPictures(string root)
        {
            for (int index = 1; index <= 16; index++)
            {
                Av1PictureSequenceTestInput.Verify(root, $"grain{index:00}", [0x81, 0, 12, 0]);
            }
            Av1PictureSequenceTestInput.Verify(root, "grainMono", [0x81, 0, 28, 0]);
        }

        internal static void VerifyOwnership(string root) =>
            Av1PictureSequenceTestInput.VerifyOwnership(root, "grain16", [0x81, 0, 12, 0]);
    }
}

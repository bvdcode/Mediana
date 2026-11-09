// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    internal static class Av1Main10TestInput
    {
        internal static void VerifyPictures(string root)
        {
            foreach (string fixture in new[] { "Default", "Odd", "Tiles", "Grain", "Superres", "SuperresGrain" })
            {
                Av1PictureSequenceTestInput.Verify(root, "main10" + fixture, [0x81, 0, 0x4D, 0]);
            }
            Av1PictureSequenceTestInput.Verify(root, "main10Mono", [0x81, 0, 0x5C, 0]);
        }

        internal static void VerifyOwnership(string root)
        {
            foreach (string fixture in new[] { "Grain", "SuperresGrain" })
            {
                Av1PictureSequenceTestInput.VerifyOwnership(root, "main10" + fixture, [0x81, 0, 0x4D, 0]);
            }
        }
    }
}

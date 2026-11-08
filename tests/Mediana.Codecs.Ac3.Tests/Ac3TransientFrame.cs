// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Ac3.Tests
{
    internal static class Ac3TransientFrame
    {
        public static byte[] CreateTailSequence() => [.. Ac3SyntheticFrame.Create(true, true),
            .. Ac3SyntheticFrame.Create(true, true, transientLocation: 4092, transientLength: 255),
            .. Ac3SyntheticFrame.Create(true, true, transientLocation: 2048, transientLength: 255)];

        public static byte[] CreateSequence()
        {
            (int? Location, int Length)[] settings = [(1408, 160), (4092, 255), (0, 128), (768, 232),
                (null, 0), (null, 0), (0, 0), (null, 0), (null, 0), (null, 0)];
            List<byte> result = [];
            foreach ((int? location, int length) in settings)
            { result.AddRange(Ac3SyntheticFrame.Create(true, true, transientLocation: location, transientLength: length)); }
            return result.ToArray();
        }
    }
}

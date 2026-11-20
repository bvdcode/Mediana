// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.OpenExr
{
    internal readonly record struct ExrChannel(string Name, ReadOnlyMemory<byte> EncodedName, int Type, bool PerceptuallyLinear, int SamplingX, int SamplingY)
    {
        internal int Bytes => Type switch
        {
            0 or 2 => 4,
            1 => 2,
            _ => throw new InvalidDataException("Invalid OpenEXR channel sample type.")
        };
    }
}

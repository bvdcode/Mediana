// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Video
{
    internal class AvcParameterSet(ReadOnlyMemory<byte> original, ReadOnlyMemory<byte> output)
    {
        public ReadOnlyMemory<byte> Original { get; } = original;
        public ReadOnlyMemory<byte> Output { get; } = output;
    }
}

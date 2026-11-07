// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Audio
{
    internal readonly ref struct VorbisMappingDefinition(ReadOnlySpan<byte> multiplex,
        ReadOnlySpan<byte> floors, ReadOnlySpan<byte> residues, ReadOnlySpan<byte> magnitude, ReadOnlySpan<byte> angle)
    {
        public ReadOnlySpan<byte> Multiplex { get; } = multiplex;
        public ReadOnlySpan<byte> Floors { get; } = floors;
        public ReadOnlySpan<byte> Residues { get; } = residues;
        public ReadOnlySpan<byte> Magnitude { get; } = magnitude;
        public ReadOnlySpan<byte> Angle { get; } = angle;
    }
}

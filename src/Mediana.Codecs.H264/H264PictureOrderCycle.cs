// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal class H264PictureOrderCycle(ReadOnlySpan<int> offsets) : IEquatable<H264PictureOrderCycle>
    {
        private readonly int[] values = offsets.ToArray();

        public static H264PictureOrderCycle Empty { get; } = new([]);
        public int Count => values.Length;
        public int this[int index] => values[index];

        public bool Equals(H264PictureOrderCycle? other) => other is not null && values.AsSpan().SequenceEqual(other.values);
        public override bool Equals(object? obj) => obj is H264PictureOrderCycle other && Equals(other);

        public override int GetHashCode()
        {
            HashCode result = new();
            foreach (int value in values)
            {
                result.Add(value);
            }
            return result.ToHashCode();
        }
    }
}

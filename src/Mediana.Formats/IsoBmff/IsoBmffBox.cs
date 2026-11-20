// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.IsoBmff
{
    /// <summary>A borrowed ISO BMFF box; its UUID identifier is separate from its payload.</summary>
    public readonly record struct IsoBmffBox(uint Type, int PayloadOffset, ReadOnlyMemory<byte> Payload, ReadOnlyMemory<byte> ExtendedType)
    {
        public static uint Code(string type)
        {
            ArgumentNullException.ThrowIfNull(type);
            if (type.Length != 4 || (type[0] | type[1] | type[2] | type[3]) > 127)
            { throw new ArgumentException("A box type must contain four ASCII characters.", nameof(type)); }
            return ((uint)type[0] << 24) | ((uint)type[1] << 16) | ((uint)type[2] << 8) | type[3];
        }
        public string TypeName => new([(char)(Type >> 24), (char)((Type >> 16) & 255), (char)((Type >> 8) & 255), (char)(Type & 255)]);
    }
}

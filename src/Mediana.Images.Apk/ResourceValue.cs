// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Images.Apk
{
    internal readonly record struct ResourceValue(byte Type, uint Data, string? Text)
    {
        public const byte Reference = 1;
        public const byte String = 3;
        public bool IsColor => Type is >= 0x1c and <= 0x1f;
        public Mediana.Drawing.Rgba32 Color(float alpha = 1) => IsColor
            ? new((byte)(Data >> 16), (byte)(Data >> 8), (byte)Data, (byte)MathF.Round((Data >> 24) * Math.Clamp(alpha, 0, 1)))
            : throw new NotSupportedException("Expected a solid Android color.");

        public static ResourceValue Read(ResourceChunk chunk, int offset, AndroidStringPool pool)
        {
            if (chunk.UInt16(offset) != 8) { throw ResourceChunk.Invalid(); }
            return Create(chunk.Byte(offset + 3), chunk.UInt32(offset + 4), pool);
        }

        public static ResourceValue Create(byte type, uint data, AndroidStringPool pool) =>
            new(type, data, type == String ? pool.Get(data) : null);
    }
}

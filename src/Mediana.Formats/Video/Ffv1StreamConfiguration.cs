// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Formats.Video
{
    /// <summary>Container dimensions followed by optional FFV1 codec private data.</summary>
    public readonly record struct Ffv1StreamConfiguration(int Width, int Height, ReadOnlyMemory<byte> ExtraData)
    {
        public const string CodecId = "ffv1";

        public static Ffv1StreamConfiguration Read(ReadOnlyMemory<byte> data)
        {
            if (data.Length < 8)
            {
                throw new InvalidDataException("FFV1 requires container picture dimensions.");
            }
            int width = BinaryPrimitives.ReadInt32LittleEndian(data.Span);
            int height = BinaryPrimitives.ReadInt32LittleEndian(data.Span[4..]);
            if (width <= 0 || height <= 0)
            {
                throw new InvalidDataException("FFV1 picture dimensions must be positive.");
            }
            return new(width, height, data[8..]);
        }

        public static byte[] Write(int width, int height, ReadOnlySpan<byte> extraData)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            byte[] result = new byte[checked(8 + extraData.Length)];
            BinaryPrimitives.WriteInt32LittleEndian(result, width);
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4), height);
            extraData.CopyTo(result.AsSpan(8));
            return result;
        }
    }
}

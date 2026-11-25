// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Formats.Video
{
    /// <summary>Coded size and codec private data for Microsoft MPEG-4 v3 and WMV2.</summary>
    public readonly record struct MicrosoftMpeg4Configuration
    {
        public const string Version3CodecId = "msmpeg4v3";
        public const string Wmv2CodecId = "wmv2";
        private const int SizeLength = 2 * sizeof(int);

        public MicrosoftMpeg4Configuration(int width, int height, ReadOnlyMemory<byte> codecPrivateData)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            Width = width;
            Height = height;
            CodecPrivateData = codecPrivateData;
        }

        public int Width { get; }
        public int Height { get; }
        public ReadOnlyMemory<byte> CodecPrivateData { get; }

        /// <summary>Writes signed 32-bit little-endian width and height, followed by codec private data.</summary>
        public byte[] Write()
        {
            byte[] data = new byte[checked(SizeLength + CodecPrivateData.Length)];
            BinaryPrimitives.WriteInt32LittleEndian(data, Width);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(sizeof(int)), Height);
            CodecPrivateData.Span.CopyTo(data.AsSpan(SizeLength));
            return data;
        }

        public static MicrosoftMpeg4Configuration Read(ReadOnlyMemory<byte> data)
        {
            if (data.Length < SizeLength)
            {
                throw new InvalidDataException("Microsoft MPEG-4 video requires a coded width and height.");
            }
            int width = BinaryPrimitives.ReadInt32LittleEndian(data.Span);
            int height = BinaryPrimitives.ReadInt32LittleEndian(data.Span[sizeof(int)..]);
            if (width <= 0 || height <= 0)
            {
                throw new InvalidDataException("Microsoft MPEG-4 video dimensions must be positive.");
            }
            return new(width, height, data[SizeLength..]);
        }
    }
}

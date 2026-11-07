// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Formats.Video
{
    /// <summary>Theora 3.2 identification header, including coded geometry and lower-left picture crop.</summary>
    public class TheoraIdentification
    {
        public const string CodecId = "theora";
        private TheoraIdentification() { }
        public int Revision { get; private init; }
        public int CodedWidth { get; private init; }
        public int CodedHeight { get; private init; }
        public int Width { get; private init; }
        public int Height { get; private init; }
        public int PictureX { get; private init; }
        public int PictureY { get; private init; }
        public uint FrameRateNumerator { get; private init; }
        public uint FrameRateDenominator { get; private init; }
        public int AspectRatioNumerator { get; private init; }
        public int AspectRatioDenominator { get; private init; }
        public int ColorSpace { get; private init; }
        public int NominalBitRate { get; private init; }
        public int Quality { get; private init; }
        public int GranuleShift { get; private init; }
        public int PixelFormat { get; private init; }
        public int HorizontalSubsampling => PixelFormat == 3 ? 1 : 2;
        public int VerticalSubsampling => PixelFormat == 0 ? 2 : 1;

        public static TheoraIdentification Read(ReadOnlySpan<byte> packet)
        {
            if (packet.Length < 42 || packet[0] != 0x80 || !packet.Slice(1, 6).SequenceEqual("theora"u8))
            { throw new InvalidDataException("The Theora identification header is absent or truncated."); }
            if (packet[7] != 3 || packet[8] != 2) { throw new NotSupportedException("The Theora bitstream version is not 3.2."); }
            int codedWidth = BinaryPrimitives.ReadUInt16BigEndian(packet[10..]) * 16;
            int codedHeight = BinaryPrimitives.ReadUInt16BigEndian(packet[12..]) * 16;
            int width = Read24(packet[14..]), height = Read24(packet[17..]);
            int x = packet[20], y = packet[21];
            uint numerator = BinaryPrimitives.ReadUInt32BigEndian(packet[22..]), denominator = BinaryPrimitives.ReadUInt32BigEndian(packet[26..]);
            int flags = BinaryPrimitives.ReadUInt16BigEndian(packet[40..]);
            int pixelFormat = (flags >> 3) & 3;
            if (codedWidth == 0 || codedHeight == 0 || width == 0 || height == 0 ||
                x + width > codedWidth || y + height > codedHeight || numerator == 0 || denominator == 0 ||
                pixelFormat == 1 || (flags & 7) != 0)
            { throw new InvalidDataException("The Theora geometry, clock or reserved identification fields are invalid."); }
            return new()
            {
                Revision = packet[9], CodedWidth = codedWidth, CodedHeight = codedHeight, Width = width, Height = height,
                PictureX = x, PictureY = y, FrameRateNumerator = numerator, FrameRateDenominator = denominator,
                AspectRatioNumerator = Read24(packet[30..]), AspectRatioDenominator = Read24(packet[33..]),
                ColorSpace = packet[36], NominalBitRate = Read24(packet[37..]),
                Quality = flags >> 10, GranuleShift = (flags >> 5) & 31, PixelFormat = pixelFormat
            };
        }

        private static int Read24(ReadOnlySpan<byte> data) => (data[0] << 16) | (data[1] << 8) | data[2];
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text;

namespace Mediana.Containers.Mp3
{
    internal static class Id3FrameParser
    {
        public static Id3TextFrame ReadText(string id, ReadOnlySpan<byte> data, int version, Mp3ReadOptions options)
        {
            if (data.IsEmpty)
            {
                throw new InvalidDataException("The ID3 text frame lacks its encoding byte.");
            }
            Id3StringReader reader = new(data[1..], data[0], version, options.MaxTextBytes);
            string? description = id == "TXXX" ? reader.ReadTerminated() : null;
            string[] values = reader.ReadValues(multiple: version == 4);
            return new Id3TextFrame(id, description, values);
        }

        public static Id3Picture ReadPicture(ReadOnlySpan<byte> data, int offset, int version, Mp3ReadOptions options)
        {
            if (data.Length < 4)
            {
                throw new InvalidDataException("The ID3 APIC frame is truncated.");
            }
            int separator = data[1..].IndexOf((byte)0);
            if (separator < 0 || separator > options.MaxTextBytes)
            {
                throw new InvalidDataException("The ID3 picture MIME type lacks a bounded terminator.");
            }
            ReadOnlySpan<byte> mime = data.Slice(1, separator);
            foreach (byte value in mime)
            {
                if (value is < 0x20 or > 0x7e)
                {
                    throw new InvalidDataException("The ID3 picture MIME type must contain printable ASCII.");
                }
            }
            string mediaType = mime.IsEmpty ? "image/" : Encoding.ASCII.GetString(mime);
            int typeOffset = separator + 2;
            if (typeOffset >= data.Length || data[typeOffset] > 20)
            {
                throw new InvalidDataException("The ID3 picture type is missing or reserved.");
            }
            int descriptionOffset = typeOffset + 1;
            Id3StringReader reader = new(data[descriptionOffset..], data[0], version, options.MaxTextBytes);
            string description = reader.ReadTerminated();
            if (version == 3 && description.EnumerateRunes().Count() > 64)
            {
                throw new InvalidDataException("The ID3v2.3 picture description exceeds 64 characters.");
            }
            int imageOffset = descriptionOffset + reader.Position;
            if (imageOffset == data.Length)
            {
                throw new InvalidDataException("The ID3 picture contains no image or URL bytes.");
            }
            return new Id3Picture(new Id3PictureInfo(data[typeOffset], mediaType, description, data.Length - imageOffset), offset + imageOffset);
        }
    }
}

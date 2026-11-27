// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Tiff;

namespace Mediana.Images.Metadata
{
    internal static class RawMakerNotePreviews
    {
        internal static void Read(ReadOnlyMemory<byte> file, bool littleEndian, TiffField note, string format, RawJpegPreviews previews, CancellationToken token)
        {
            ReadOnlySpan<byte> header = note.Bytes.Span;
            if (header.StartsWith("Nikon\0\x02"u8))
            {
                if (header.Length < 18) { throw new InvalidDataException("Truncated Nikon MakerNote header."); }
                TiffDirectoryReader reader = new(file[checked((int)note.Offset + 10)..], includeField: PreviewIfdField);
                TiffDirectory directory = reader.Read(reader.FirstOffset, token);
                if (directory.Fields.ContainsKey(17))
                { previews.ReadDirectory(reader, reader.Read(directory.Number(17), token)); }
            }
            else if (header.StartsWith("OLYMPUS\0"u8))
            {
                TiffDirectoryReader reader = new(file[checked((int)note.Offset)..], ByteOrder(header, 8), 12, 12, OlympusPreviewField);
                ReadOlympus(reader, reader.Read(reader.FirstOffset, token), previews, token);
            }
            else if (header.StartsWith("OM SYSTEM\0"u8))
            {
                TiffDirectoryReader reader = new(file[checked((int)note.Offset)..], ByteOrder(header, 10), 16, 16, OlympusPreviewField);
                ReadOlympus(reader, reader.Read(reader.FirstOffset, token), previews, token);
            }
            else if (header.StartsWith("OLYMP\0"u8))
            {
                TiffDirectoryReader reader = new(file, littleEndian, note.Offset + 8, 8, OlympusPreviewField);
                ReadOlympus(reader, reader.Read(reader.FirstOffset, token), previews, token);
            }
            else if (header.StartsWith("AOC\0"u8))
            {
                TiffDirectoryReader reader = new(file, ByteOrder(header, 4), note.Offset + 6, 8, tag => tag is 3 or 4);
                previews.ReadPair(reader, reader.Read(reader.FirstOffset, token), 4, 3);
            }
            else if (header.StartsWith("PENTAX \0"u8))
            {
                TiffDirectoryReader reader = new(file[checked((int)note.Offset)..], ByteOrder(header, 8), 10, 10, tag => tag is 3 or 4);
                TiffDirectory directory = reader.Read(reader.FirstOffset, token);
                previews.ReadPair(reader, directory, 4, 3);
            }
            else if (format == "srw" && header.Length >= 14 && header.Slice(10, 4).SequenceEqual("0100"u8))
            {
                TiffDirectoryReader reader = new(file[checked((int)note.Offset)..], littleEndian, 0, 0, PreviewIfdField);
                TiffDirectory directory = reader.Read(0, token);
                ulong offset = directory.Number(53);
                if (offset != 0)
                { previews.ReadDirectory(reader, reader.Read(offset, token)); }
            }
        }

        private static bool OlympusPreviewField(ushort tag) => tag is 129 or 136 or 137 or 256 or 257 or 258 or 4150 or 4151 or 8224;
        private static bool PreviewIfdField(ushort tag) => tag is 17 or 53 or 259 or 262 or 273 or 274 or 279 or 347 or 513 or 514;

        private static bool ByteOrder(ReadOnlySpan<byte> header, int offset)
        {
            if (header.Length < offset + 2) { throw new InvalidDataException("Truncated MakerNote byte order."); }
            return header.Slice(offset, 2) switch
            {
                [73, 73] => true,
                [77, 77] => false,
                _ => throw new InvalidDataException("Invalid MakerNote byte order.")
            };
        }

        private static void ReadOlympus(TiffDirectoryReader reader, TiffDirectory directory, RawJpegPreviews previews, CancellationToken token)
        {
            if (directory.Fields.TryGetValue(256, out TiffField? thumbnail)) { previews.Add(thumbnail.Bytes); }
            if (directory.Fields.TryGetValue(129, out TiffField? preview)) { previews.Add(preview.Bytes); }
            previews.ReadPair(reader, directory, 136, 137);
            previews.ReadPair(reader, directory, 4150, 4151);
            if (directory.Fields.TryGetValue(8224, out TiffField? settings) && settings.Type is 4 or 13)
            {
                TiffDirectory camera = reader.Read(settings.Unsigned(), token);
                if (camera.Number(256, 1) != 0) { previews.ReadPair(reader, camera, 257, 258); }
            }
        }
    }
}

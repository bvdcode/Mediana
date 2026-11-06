// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Formats.Tiff
{
    /// <summary>Shared borrowed TIFF directory structure for image decoding and Exif metadata.</summary>
    internal class TiffDirectoryReader
    {
        private readonly ReadOnlyMemory<byte> data;
        private readonly Dictionary<ulong, TiffDirectory> directories = [];
        internal bool LittleEndian { get; }
        internal bool BigTiff { get; }
        internal ulong FirstOffset { get; }
        internal int HeaderLength => BigTiff ? 16 : 8;
        internal TiffDirectoryReader(ReadOnlyMemory<byte> data)
        {
            this.data = data;
            LittleEndian = data.Span switch
            {
                [73, 73, ..] => true,
                [77, 77, ..] => false,
                _ => throw new InvalidDataException("Invalid Exif TIFF byte order.")
            };
            ushort version = U16(2);
            BigTiff = version switch
            {
                42 => false,
                43 => true,
                _ => throw new InvalidDataException("Invalid Exif TIFF identifier.")
            };
            if (BigTiff && (U16(4) != 8 || U16(6) != 0)) { throw new InvalidDataException("Invalid BigTIFF offset header."); }
            FirstOffset = BigTiff ? U64(8) : U32(4);
        }
        internal ReadOnlyMemory<byte> Slice(ulong offset, ulong length)
        {
            if (offset > (ulong)data.Length || length > (ulong)data.Length - offset)
            { throw new InvalidDataException("Exif directory or value exceeds its data extent."); }
            return data.Slice((int)offset, (int)length);
        }
        internal TiffDirectory Read(ulong offset, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (directories.TryGetValue(offset, out TiffDirectory? directory)) { return directory; }
            if (offset < (ulong)HeaderLength) { throw new InvalidDataException("Exif directory overlaps the TIFF header."); }
            int countSize = BigTiff ? 8 : 2, entrySize = BigTiff ? 20 : 12, inline = BigTiff ? 8 : 4;
            ulong count = BigTiff ? U64(offset) : U16(offset);
            if (count > (ulong)data.Length / (uint)entrySize) { throw new InvalidDataException("TIFF directory count exceeds its input."); }
            Slice(offset, (ulong)countSize + count * (uint)entrySize + (uint)inline);
            Dictionary<ushort, TiffField> fields = [];
            for (ulong index = 0; index < count; index++)
            {
                token.ThrowIfCancellationRequested();
                ulong entry = offset + (uint)countSize + index * (uint)entrySize;
                ushort tag = U16(entry), type = U16(entry + 2);
                ulong elements = BigTiff ? U64(entry + 4) : U32(entry + 4);
                int size = TiffField.Size(type);
                if (elements > (ulong)data.Length / (uint)size) { throw new InvalidDataException("Exif value exceeds the supported byte range."); }
                ulong bytes = elements * (uint)size, location = entry + (BigTiff ? 12u : 8u);
                if (bytes > (uint)inline)
                {
                    location = BigTiff ? U64(location) : U32(location);
                    if (location < (ulong)HeaderLength) { throw new InvalidDataException("Exif value overlaps the TIFF header."); }
                }
                if (!fields.TryAdd(tag, new(tag, type, elements, Slice(location, bytes), LittleEndian)))
                { throw new InvalidDataException("Duplicate TIFF directory tag."); }
            }
            ulong next = offset + (uint)countSize + count * (uint)entrySize;
            directory = new(fields, BigTiff ? U64(next) : U32(next));
            directories.Add(offset, directory); return directory;
        }
        private ushort U16(ulong position) => LittleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(Slice(position, 2).Span) : BinaryPrimitives.ReadUInt16BigEndian(Slice(position, 2).Span);
        private uint U32(ulong position) => LittleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(Slice(position, 4).Span) : BinaryPrimitives.ReadUInt32BigEndian(Slice(position, 4).Span);
        private ulong U64(ulong position) => LittleEndian ? BinaryPrimitives.ReadUInt64LittleEndian(Slice(position, 8).Span) : BinaryPrimitives.ReadUInt64BigEndian(Slice(position, 8).Span);
    }
}

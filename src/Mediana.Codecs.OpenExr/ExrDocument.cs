// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Codecs.OpenExr
{
    internal class ExrDocument
    {
        internal const uint Magic = 20000630;
        internal ReadOnlyMemory<byte> Encoded { get; }
        internal List<ExrHeader> Parts { get; } = [];
        internal bool Multipart { get; }
        internal int PixelDataOffset { get; }
        internal ExrDocument(ReadOnlyMemory<byte> encoded, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Encoded = encoded;
            ExrCursor cursor = new(encoded);
            if ((uint)cursor.Int() != Magic) { throw new InvalidDataException("Invalid OpenEXR signature."); }
            uint version = (uint)cursor.Int();
            if ((version & 255) != 2 || (version & ~0x1e02u) != 0) { throw new NotSupportedException("Unknown OpenEXR file version or flags."); }
            Multipart = (version & 0x1000) != 0;
            bool tiled = (version & 0x200) != 0;
            bool deep = (version & 0x800) != 0;
            if (tiled && (Multipart || deep)) { throw new InvalidDataException("Multipart or deep OpenEXR cannot set the single-part tiled flag."); }
            HashSet<string> names = new(StringComparer.Ordinal);
            do
            {
                ExrHeader part = ExrHeader.Read(ref cursor, (version & 0x400) != 0 ? 255 : 31, Multipart || deep, tiled, token);
                if (Multipart && !names.Add(part.Name)) { throw new InvalidDataException("Duplicate OpenEXR part name."); }
                Parts.Add(part);
                if (!Multipart) { break; }
                if (cursor.Remaining == 0) { throw new InvalidDataException("Missing OpenEXR multipart header terminator."); }
                if (encoded.Span[cursor.Position] == 0) { cursor.Byte(); break; }
            } while (true);
            bool hasDeepPart = Parts.Any(part => part.Type is "deepscanline" or "deeptile");
            if (deep != hasDeepPart || !Multipart && !deep && tiled != (Parts[0].Type == "tiledimage"))
            { throw new InvalidDataException("OpenEXR part types disagree with the file flags."); }
            foreach (ExrHeader part in Parts)
            {
                token.ThrowIfCancellationRequested();
                if (part.ChunkCount > cursor.Remaining / 8) { throw new InvalidDataException("OpenEXR chunk table exceeds its input."); }
                part.Offsets = cursor.Take(part.ChunkCount * 8);
            }
            PixelDataOffset = cursor.Position;
        }

        internal ExrCursor Chunk(ExrHeader part, int index)
        {
            ulong offset = BinaryPrimitives.ReadUInt64LittleEndian(part.Offsets.Span[(index * 8)..]);
            if (offset < (uint)PixelDataOffset || offset > (ulong)Encoded.Length - 8)
            { throw new InvalidDataException("OpenEXR chunk offset is invalid or incomplete."); }
            return new(Encoded[(int)offset..]);
        }
    }
}

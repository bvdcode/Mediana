// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core.Metadata;

namespace Mediana.Codecs.Gif
{
    internal class GifDocument
    {
        internal readonly int Width, Height, BackgroundIndex, Aspect;
        internal readonly ReadOnlyMemory<byte> Palette;
        internal readonly List<GifFrame> Frames = [];
        internal readonly List<MediaProperty> Properties = [];
        internal long Duration;
        private readonly ReadOnlyMemory<byte> data;
        private int position;
        private readonly CancellationToken token;

        internal GifDocument(ReadOnlyMemory<byte> data, CancellationToken token)
        {
            this.data = data; this.token = token;
            if (data.Length < 13 || !(data.Span.StartsWith("GIF87a"u8) || data.Span.StartsWith("GIF89a"u8)))
            { throw new InvalidDataException("Invalid GIF header."); }
            Width = U16(6); Height = U16(8); BackgroundIndex = data.Span[11]; Aspect = data.Span[12];
            if (Width == 0 || Height == 0) { throw new InvalidDataException("Invalid GIF dimensions."); }
            int flags = data.Span[10]; position = 13;
            Palette = (flags & 128) == 0 ? default : Take(3 << ((flags & 7) + 1));
            int delay = 0, disposal = 0, transparent = -1;
            Properties.Add(new("gif", "version", System.Text.Encoding.ASCII.GetString(data.Span[..6])));
            bool finished = false;
            while (position < data.Length && !finished)
            {
                token.ThrowIfCancellationRequested();
                int kind = Byte();
                switch (kind)
                {
                    case 0x3b: finished = true; break;
                    case 0x21:
                        int label = Byte();
                        switch (label)
                        {
                            case 0xf9:
                                if (Byte() != 4) { throw new InvalidDataException("Invalid GIF graphics control block."); }
                                int control = Byte(); delay = Byte(); delay |= Byte() << 8;
                                transparent = Byte(); disposal = (control >> 2) & 7;
                                if ((control & 1) == 0) { transparent = -1; }
                                if (Byte() != 0 || disposal > 3) { throw new InvalidDataException("Invalid GIF graphics control termination or disposal."); }
                                break;
                            case 0xff:
                                int size = Byte(); ReadOnlyMemory<byte> application = Take(size);
                                ReadOnlyMemory<byte> extension = Blocks();
                                if (application.Span.SequenceEqual("NETSCAPE2.0"u8) || application.Span.SequenceEqual("ANIMEXTS1.0"u8))
                                {
                                    if (extension.Length < 5 || extension.Span[0] != 3 || extension.Span[1] != 1)
                                    { throw new InvalidDataException("Invalid GIF loop extension."); }
                                    Properties.Add(new("gif", "loop_count", (int)BinaryPrimitives.ReadUInt16LittleEndian(extension.Span[2..])));
                                }
                                break;
                            case 0xfe:
                                ReadOnlyMemory<byte> comment = Blocks();
                                Properties.Add(new("gif", "comment_blocks", MediaPropertyValue.FromBytes(comment.Span)));
                                break;
                            case 0x01:
                                throw new NotSupportedException("GIF plain-text rendering is not supported.");
                            default: Blocks(); break;
                        }
                        break;
                    case 0x2c:
                        int left = Byte(), top, width, height;
                        left |= Byte() << 8; top = Byte(); top |= Byte() << 8;
                        width = Byte(); width |= Byte() << 8; height = Byte(); height |= Byte() << 8;
                        int packed = Byte();
                        if (width == 0 || height == 0 || left > Width - width || top > Height - height)
                        { throw new InvalidDataException("GIF frame exceeds its logical screen."); }
                        ReadOnlyMemory<byte> palette = (packed & 128) != 0 ? Take(3 << ((packed & 7) + 1)) : Palette;
                        if (palette.IsEmpty || transparent >= palette.Length / 3) { throw new InvalidDataException("GIF frame has no usable palette."); }
                        int minimum = Byte();
                        if (minimum is < 2 or > 8) { throw new InvalidDataException("Invalid GIF LZW minimum code size."); }
                        Frames.Add(new(left, top, width, height, (packed & 64) != 0, delay, disposal, transparent, palette, minimum, Blocks()));
                        Duration = checked(Duration + delay);
                        delay = disposal = 0; transparent = -1;
                        break;
                    default: throw new InvalidDataException("Invalid GIF block identifier.");
                }
            }
            if (!finished || Frames.Count == 0) { throw new InvalidDataException("GIF is missing an image or its trailer."); }
        }
        private int U16(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data.Span[offset..]);
        private int Byte()
        {
            if (position >= data.Length) { throw new InvalidDataException("Truncated GIF block."); }
            return data.Span[position++];
        }
        private ReadOnlyMemory<byte> Take(int count)
        {
            if (count > data.Length - position) { throw new InvalidDataException("Truncated GIF payload."); }
            ReadOnlyMemory<byte> value = data.Slice(position, count); position += count; return value;
        }
        private ReadOnlyMemory<byte> Blocks()
        {
            int start = position;
            int count;
            while ((count = Byte()) != 0) { token.ThrowIfCancellationRequested(); Take(count); }
            return data[start..position];
        }
    }
}

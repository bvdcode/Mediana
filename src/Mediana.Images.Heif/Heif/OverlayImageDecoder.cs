// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Collections.Generic;
using System.IO;
using Mediana.Images.Heif.Binary;

namespace Mediana.Images.Heif.Container
{
    internal static class OverlayImageDecoder
    {
        public static async Task<RgbaImage> DecodeAsync(HeifContainer container, HeifItem item, ImageProperties properties,
            Func<uint, PixelBufferScope, Task<RgbaImage>> decode, PixelBufferScope buffers, CancellationToken cancellationToken)
        {
            ByteReader reader = new ByteReader(container.ReadData(item.Id));
            if (reader.U8() != 0)
            {
                throw new UnsupportedHeicFeatureException("image overlay version");
            }
            byte flags = reader.U8();
            if (flags > 1)
            {
                throw new UnsupportedHeicFeatureException("image overlay flags");
            }
            byte[] fill = new byte[4];
            for (int channel = 0; channel < fill.Length; channel++)
            {
                fill[channel] = (byte)(((reader.U16() * 255) + (ushort.MaxValue / 2)) / ushort.MaxValue);
            }
            int fieldBytes = (flags + 1) * 2;
            ulong width = reader.UInt(fieldBytes);
            ulong height = reader.UInt(fieldBytes);
            if ((width != (ulong)properties.Width) || (height != (ulong)properties.Height))
            {
                throw new InvalidDataException("Overlay header disagrees with image spatial extents.");
            }
            if (!item.References.TryGetValue(HeifBox.Code("dimg"), out IReadOnlyList<uint>? inputs) || (inputs.Count == 0) ||
                ((reader.Remaining / (fieldBytes * 2)) != inputs.Count) || ((reader.Remaining % (fieldBytes * 2)) != 0))
            {
                throw new InvalidDataException("Overlay offsets disagree with its input count.");
            }
            Memory<byte> pixels = buffers.Allocate(checked(properties.Width * properties.Height * 4));
            for (int offset = 0; offset < pixels.Length; offset += 4)
            {
                fill.AsSpan().CopyTo(pixels.Span.Slice(offset, 4));
            }
            ImageColorInformation? color = null;
            foreach (uint id in inputs)
            {
                int left = ReadOffset(reader, fieldBytes);
                int top = ReadOffset(reader, fieldBytes);
                using PixelBufferScope inputBuffers = new PixelBufferScope(true);
                cancellationToken.ThrowIfCancellationRequested();
                RgbaImage image = await decode(id, inputBuffers);
                if (color is null)
                {
                    color = image.ColorInformation;
                    ValidateFillColor(fill, color);
                }
                else if (!color.Matches(image.ColorInformation))
                {
                    throw new UnsupportedHeicFeatureException("overlay inputs with different colour information");
                }
                Composite(image, pixels.Span, properties.Width, properties.Height, left, top);
            }
            return new RgbaImage(properties.Width, properties.Height, pixels, color!);
        }

        private static int ReadOffset(ByteReader reader, int fieldBytes)
        {
            switch (fieldBytes)
            {
                case 2:
                    return unchecked((short)reader.U16());
                case 4:
                    return unchecked((int)reader.U32());
                default:
                    throw new ArgumentOutOfRangeException(nameof(fieldBytes));
            }
        }

        private static void ValidateFillColor(ReadOnlySpan<byte> fill, ImageColorInformation color)
        {
            if ((fill[3] == 0) || ((fill[0] == 0) && (fill[1] == 0) && (fill[2] == 0)))
            {
                return;
            }
            bool srgb = (color.ColorPrimaries == 1) && (color.TransferCharacteristics == 13);
            bool unspecified = (color.ColorPrimaries == 2) && (color.TransferCharacteristics == 2);
            if (!color.IccProfile.IsEmpty || (!srgb && !unspecified))
            {
                throw new UnsupportedHeicFeatureException("overlay canvas colour conversion");
            }
        }

        private static void Composite(RgbaImage image, Span<byte> output, int width, int height, int left, int top)
        {
            int startX = (int)Math.Min(width, Math.Max(0L, left));
            int startY = (int)Math.Min(height, Math.Max(0L, top));
            int endX = (int)Math.Max(0L, Math.Min(width, (long)left + image.Width));
            int endY = (int)Math.Max(0L, Math.Min(height, (long)top + image.Height));
            if ((startX >= endX) || (startY >= endY))
            {
                return;
            }
            for (int y = startY; y < endY; y++)
            {
                ReadOnlySpan<byte> source = image.Pixels.Span.Slice(checked(((((y - top) * image.Width) + startX) - left) * 4));
                Span<byte> target = output.Slice(((y * width) + startX) * 4, (endX - startX) * 4);
                for (int offset = 0; offset < target.Length; offset += 4)
                {
                    int sourceAlpha = source[offset + 3];
                    int targetWeight = target[offset + 3] * (255 - sourceAlpha);
                    int alpha = (sourceAlpha * 255) + targetWeight;
                    if (alpha == 0)
                    {
                        target.Slice(offset, 4).Clear();
                        continue;
                    }
                    for (int channel = 0; channel < 3; channel++)
                    {
                        int value = (source[offset + channel] * sourceAlpha * 255) + (target[offset + channel] * targetWeight);
                        target[offset + channel] = (byte)((value + (alpha / 2)) / alpha);
                    }
                    target[offset + 3] = (byte)((alpha + 127) / 255);
                }
            }
        }
    }
}

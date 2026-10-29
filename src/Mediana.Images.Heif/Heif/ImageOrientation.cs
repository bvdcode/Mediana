// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Mediana.Images.Heif.Binary;

namespace Mediana.Images.Heif.Container
{
    /// <summary>
    /// Maps source pixels through a sequence of quarter turns and axis reflections.
    /// </summary>
    /// <remarks>
    /// The integer basis remains orthogonal, so disjoint source tiles write disjoint
    /// destination pixels. The mapping is frozen before workers start copying tiles.
    /// Cropping ends a prefix because it changes the source rectangle.
    /// </remarks>
    internal class ImageOrientation
    {
        private const int CopyBlockSize = 32;
        private int xx = 1;
        private int xy;
        private int yx;
        private int yy = 1;
        private int originX;
        private int originY;

        private ImageOrientation(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public int Width { get; private set; }
        public int Height { get; private set; }
        public int PropertyCount { get; private set; }

        public static ImageOrientation Create(int width, int height, int turns, int? mirrorAxis)
        {
            ImageOrientation result = new ImageOrientation(width, height);
            result.Append(turns, mirrorAxis);
            return result;
        }

        public static ImageOrientation ReadPrefix(int width, int height, IReadOnlyList<HeifBox> properties)
        {
            ImageOrientation result = new ImageOrientation(width, height);
            foreach (HeifBox property in properties)
            {
                if (!result.Append(property))
                {
                    break;
                }
            }
            return result;
        }

        public static ImageOrientation Read(int width, int height, HeifBox property)
        {
            ImageOrientation result = new ImageOrientation(width, height);
            if (!result.Append(property))
            {
                throw new InvalidDataException("Expected an image orientation.");
            }
            return result;
        }

        public void CopyTile(RgbaImage source, Span<byte> destination, int left, int top, int width, int height)
        {
            int offset = (originY * Width) + originX;
            int stepX = (yx * Width) + xx;
            int stepY = (yy * Width) + xy;
            if (stepX == 1)
            {
                for (int y = 0; y < height; y++)
                {
                    source.Pixels.Span.Slice(y * source.Stride, width * 4).CopyTo(
                        destination.Slice((offset + left + ((top + y) * stepY)) * 4, width * 4));
                }
                return;
            }

            ReadOnlySpan<uint> input = MemoryMarshal.Cast<byte, uint>(source.Pixels.Span);
            Span<uint> output = MemoryMarshal.Cast<byte, uint>(destination);
            for (int blockY = 0; blockY < height; blockY += CopyBlockSize)
            {
                int endY = Math.Min(height, blockY + CopyBlockSize);
                for (int blockX = 0; blockX < width; blockX += CopyBlockSize)
                {
                    int endX = Math.Min(width, blockX + CopyBlockSize);
                    for (int y = blockY; y < endY; y++)
                    {
                        int target = offset + ((top + y) * stepY) + ((left + blockX) * stepX);
                        int start = y * source.Width;
                        for (int x = blockX; x < endX; x++)
                        {
                            output[target] = input[start + x];
                            target += stepX;
                        }
                    }
                }
            }
        }

        private bool Append(HeifBox property)
        {
            bool rotation = property.Type == HeifBox.Code("irot");
            if (!rotation && (property.Type != HeifBox.Code("imir")))
            {
                return false;
            }

            ByteReader reader = new ByteReader(property.Payload);
            int value = reader.U8();
            if (value > (rotation ? 3 : 1))
            {
                throw new InvalidDataException("Nonzero image orientation reserved bits.");
            }
            if (reader.Remaining != 0)
            {
                throw new InvalidDataException("Unexpected image transformation data.");
            }

            if (rotation)
            {
                Append(value, null);
            }
            else
            {
                Append(0, value);
            }
            PropertyCount++;
            return true;
        }

        private void Append(int turns, int? mirrorAxis)
        {
            if ((turns < 0) || (turns > 3))
            {
                throw new ArgumentOutOfRangeException(nameof(turns));
            }
            for (int turn = 0; turn < turns; turn++)
            {
                (originX, originY) = (originY, Width - 1 - originX);
                (xx, xy, yx, yy) = (yx, yy, -xx, -xy);
                (Width, Height) = (Height, Width);
            }

            switch (mirrorAxis)
            {
                case null:
                    break;
                case 0:
                    originY = Height - 1 - originY;
                    yx = -yx;
                    yy = -yy;
                    break;
                case 1:
                    originX = Width - 1 - originX;
                    xx = -xx;
                    xy = -xy;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mirrorAxis));
            }
        }
    }
}

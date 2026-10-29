// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Collections.Generic;
using System.IO;
using Mediana.Images.Heif.Binary;

namespace Mediana.Images.Heif.Container
{
    /// <summary>
    /// Locates an integral clean aperture inside the untransformed image.
    /// </summary>
    internal readonly struct ImageCrop
    {
        private ImageCrop(int left, int top, int width, int height, int propertyCount)
        {
            Left = left;
            Top = top;
            Width = width;
            Height = height;
            PropertyCount = propertyCount;
        }

        public int Left { get; }
        public int Top { get; }
        public int Width { get; }
        public int Height { get; }
        public int PropertyCount { get; }

        public static ImageCrop ReadPrefix(int width, int height, IReadOnlyList<HeifBox> properties)
        {
            if ((properties.Count != 0) && (properties[0].Type == HeifBox.Code("clap")))
            {
                return Read(width, height, properties[0]);
            }
            return new ImageCrop(0, 0, width, height, 0);
        }

        public static ImageCrop Read(int width, int height, HeifBox property)
        {
            if (property.Type != HeifBox.Code("clap"))
            {
                throw new InvalidDataException("Expected a clean aperture.");
            }
            ByteReader reader = new ByteReader(property.Payload);
            int croppedWidth = Dimension(reader, width);
            int croppedHeight = Dimension(reader, height);
            long left = Origin(reader, width, croppedWidth);
            long top = Origin(reader, height, croppedHeight);
            if ((left < 0) || (top < 0) || ((left + croppedWidth) > width) || ((top + croppedHeight) > height))
            {
                throw new InvalidDataException("Clean aperture exceeds the image.");
            }
            if (reader.Remaining != 0)
            {
                throw new InvalidDataException("Unexpected image transformation data.");
            }
            return new ImageCrop((int)left, (int)top, croppedWidth, croppedHeight, 1);
        }

        private static int Dimension(ByteReader reader, int extent)
        {
            uint numerator = reader.U32();
            uint denominator = reader.U32();
            if (denominator == 0)
            {
                throw new InvalidDataException("Clean aperture denominator must be nonzero.");
            }
            if ((numerator % denominator) != 0)
            {
                throw new UnsupportedHeicFeatureException("fractional clean aperture dimensions");
            }
            uint result = numerator / denominator;
            if ((result == 0) || (result > extent))
            {
                throw new InvalidDataException("Clean aperture exceeds the image.");
            }
            return (int)result;
        }

        private static long Origin(ByteReader reader, int extent, int croppedExtent)
        {
            int offset = unchecked((int)reader.U32());
            uint denominator = reader.U32();
            if (denominator == 0)
            {
                throw new InvalidDataException("Clean aperture denominator must be nonzero.");
            }
            long numerator = ((long)(extent - croppedExtent) * denominator) + (2L * offset);
            long divisor = 2L * denominator;
            if ((numerator % divisor) != 0)
            {
                throw new UnsupportedHeicFeatureException("fractional clean aperture offsets");
            }
            return numerator / divisor;
        }
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;
using Mediana.Formats.Tiff;

namespace Mediana.Formats.Metadata
{
    /// <summary>Reads the primary TIFF/Exif orientation without interpreting thumbnail or vendor directories.</summary>
    public static class ExifOrientation
    {
        public static OrthogonalTransform Read(ReadOnlyMemory<byte> tiff, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TiffDirectoryReader reader = new(tiff);
            if (reader.BigTiff) { throw new InvalidDataException("Exif requires a classic TIFF header."); }
            TiffDirectory directory = reader.Read(reader.FirstOffset, cancellationToken);
            if (!directory.Fields.TryGetValue(274, out TiffField? field)) { return OrthogonalTransform.Identity; }
            if (field.Type != 3 || field.Count != 1) { throw new InvalidDataException("Invalid Exif orientation type or count."); }
            return ToTransform(checked((int)field.Unsigned(0)));
        }

        /// <summary>Maps TIFF tag 274 values 1 through 8 to exact display rotations and reflections.</summary>
        public static OrthogonalTransform ToTransform(int orientation) => OrthogonalTransform.FromMatrix(orientation switch
        {
            1 => VideoDisplayMatrix.Identity,
            2 => new(-1, 0, 0, 0, 1, 0, 0, 0, 1),
            3 => new(-1, 0, 0, 0, -1, 0, 0, 0, 1),
            4 => new(1, 0, 0, 0, -1, 0, 0, 0, 1),
            5 => new(0, 1, 0, 1, 0, 0, 0, 0, 1),
            6 => new(0, 1, 0, -1, 0, 0, 0, 0, 1),
            7 => new(0, -1, 0, -1, 0, 0, 0, 0, 1),
            8 => new(0, -1, 0, 1, 0, 0, 0, 0, 1),
            _ => throw new InvalidDataException("Reserved Exif orientation.")
        });
    }
}

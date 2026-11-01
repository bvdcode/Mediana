// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers
{
    /// <summary>Immutable picture declarations in container order. They do not validate image bytes.</summary>
    public class ContainerPictureInfo
    {
        public ContainerPictureInfo(int byteLength, PictureDataKind dataKind, string? mediaType = null,
            string? description = null, uint? pictureType = null)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(byteLength);
            switch (dataKind)
            {
                case PictureDataKind.Embedded:
                case PictureDataKind.ExternalReference:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(dataKind));
            }
            ByteLength = byteLength;
            DataKind = dataKind;
            MediaType = mediaType;
            Description = description;
            PictureType = pictureType;
        }

        public int ByteLength { get; }
        /// <summary>Whether the original bytes embed a picture or describe an external resource.</summary>
        public PictureDataKind DataKind { get; }
        /// <summary>Declared media type; null means unspecified. No content detection is performed.</summary>
        public string? MediaType { get; }
        public string? Description { get; }
        /// <summary>Original ID3/FLAC picture type number, when declared. No preferred cover is selected.</summary>
        public uint? PictureType { get; }
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Matroska
{
    internal static class MatroskaVideoReader
    {
        private static ReadOnlySpan<uint> Fields => [0xB0, 0xBA, 0x54AA, 0x54BB, 0x54CC, 0x54DD,
            0x54B0, 0x54BA, 0x54B2, 0x9A, 0x9D, 0x53B8, 0x53C0, 0x55B0, 0x7670];

        public static async ValueTask<MatroskaVideoSettings> ReadAsync(MatroskaMetadataReader reader,
            EbmlElement master, CancellationToken token)
        {
            reader.ValidateMaster(master, 0xE0);
            ulong width = 0;
            ulong height = 0;
            ulong bottom = 0;
            ulong top = 0;
            ulong left = 0;
            ulong right = 0;
            ulong unit = 0;
            ulong interlaced = 0;
            ulong fieldOrder = 2;
            ulong stereoMode = 0;
            ulong alphaMode = 0;
            EbmlElement? displayWidth = null;
            EbmlElement? displayHeight = null;
            EbmlElement? colour = null;
            EbmlElement? projection = null;
            MatroskaFieldSet seen = new();
            long offset = master.DataOffset;
            while (await reader.ReadChildAsync(master, offset, token).ConfigureAwait(false) is EbmlElement child)
            {
                offset = child.EndOffset!.Value;
                if (!seen.TryMark(child.Id, Fields))
                {
                    continue;
                }
                switch (child.Id)
                {
                    case 0xB0: width = await reader.ReadUnsignedAsync(child, 0, token).ConfigureAwait(false); break;
                    case 0xBA: height = await reader.ReadUnsignedAsync(child, 0, token).ConfigureAwait(false); break;
                    case 0x54AA: bottom = await reader.ReadUnsignedAsync(child, 0, token).ConfigureAwait(false); break;
                    case 0x54BB: top = await reader.ReadUnsignedAsync(child, 0, token).ConfigureAwait(false); break;
                    case 0x54CC: left = await reader.ReadUnsignedAsync(child, 0, token).ConfigureAwait(false); break;
                    case 0x54DD: right = await reader.ReadUnsignedAsync(child, 0, token).ConfigureAwait(false); break;
                    case 0x54B0: displayWidth = child; break;
                    case 0x54BA: displayHeight = child; break;
                    case 0x54B2: unit = await reader.ReadUnsignedAsync(child, 0, token).ConfigureAwait(false); break;
                    case 0x9A: interlaced = await reader.ReadUnsignedAsync(child, 0, token).ConfigureAwait(false); break;
                    case 0x9D: fieldOrder = await reader.ReadUnsignedAsync(child, 2, token).ConfigureAwait(false); break;
                    case 0x53B8: stereoMode = await reader.ReadUnsignedAsync(child, 0, token).ConfigureAwait(false); break;
                    case 0x53C0: alphaMode = await reader.ReadUnsignedAsync(child, 0, token).ConfigureAwait(false); break;
                    case 0x55B0: colour = child; break;
                    case 0x7670: projection = child; break;
                }
            }
            if (width == 0 || height == 0 || left >= width || right >= width - left ||
                top >= height || bottom >= height - top || interlaced > 2)
            {
                throw new InvalidDataException("Matroska video dimensions, cropping or interlace declaration is invalid.");
            }
            if (interlaced == 1 && fieldOrder is not (0 or 1 or 2 or 6 or 9 or 14))
            {
                throw new InvalidDataException("The Matroska interlaced field order is invalid.");
            }
            ulong? displayedWidth = await ReadDisplayAsync(reader, displayWidth, unit, width - left - right, token).ConfigureAwait(false);
            ulong? displayedHeight = await ReadDisplayAsync(reader, displayHeight, unit, height - top - bottom, token).ConfigureAwait(false);
            return new()
            {
                PixelWidth = width,
                PixelHeight = height,
                CropBottom = bottom,
                CropTop = top,
                CropLeft = left,
                CropRight = right,
                DisplayWidth = displayedWidth,
                DisplayHeight = displayedHeight,
                DisplayUnit = unit,
                Interlaced = interlaced,
                FieldOrder = fieldOrder,
                StereoMode = stereoMode,
                AlphaMode = alphaMode,
                Colour = colour is EbmlElement colourElement ?
                    await MatroskaColourReader.ReadAsync(reader, colourElement, token).ConfigureAwait(false) : null,
                Projection = projection
            };
        }

        private static async ValueTask<ulong?> ReadDisplayAsync(MatroskaMetadataReader reader,
            EbmlElement? element, ulong unit, ulong croppedPixels, CancellationToken token)
        {
            ulong? value = null;
            if (unit == 0)
            {
                value = croppedPixels;
            }
            if (element is EbmlElement field)
            {
                value = await reader.ReadUnsignedAsync(field, value ?? 0, token).ConfigureAwait(false);
                if (value == 0)
                {
                    throw new InvalidDataException("Matroska video display dimensions must be positive.");
                }
            }
            return value;
        }
    }
}

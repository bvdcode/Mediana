// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core.Video;

namespace Mediana.Codecs.Jpeg
{
    internal partial class JpegMetadata
    {
        private bool jfif;
        private int? adobeTransform;
        private int iccCount;
        private int iccParts;
        private ReadOnlyMemory<byte>[]? iccFragments;
        private bool exif;
        private bool exifCustomColor;
        private ushort? exifColorSpace;
        private readonly bool[] iccSeen = new bool[256];
        internal JpegColorSpace ColorSpace { get; private set; } = JpegColorSpace.YCbCr;
        internal bool HasIcc => iccCount != 0;
        internal int? AdobeColorTransform => adobeTransform;
        internal SampleAspectRatio? Aspect { get; private set; }
        internal OrthogonalTransform Orientation { get; private set; } = OrthogonalTransform.Identity;
        internal bool Centered { get; private set; } = true;
        internal bool UsedJfifChromaPosition { get; private set; }
        internal bool HasUnalignedExifOffsets { get; private set; }
        internal bool IsSrgb => exifColorSpace == 1;
        internal bool Uncalibrated => exifColorSpace == 65535;

        internal void Read(byte marker, ReadOnlyMemory<byte> data, bool allowUnalignedExifOffsets, CancellationToken token)
        {
            ReadOnlySpan<byte> segment = data.Span;
            if (marker == 224 && segment.StartsWith("JFIF\0"u8))
            {
                if (jfif || segment.Length < 14 || segment[5] != 1 || segment[7] > 2 ||
                    segment.Length != 14 + 3 * segment[12] * segment[13])
                {
                    throw new InvalidDataException("JPEG JFIF version, density, thumbnail extent or uniqueness is invalid.");
                }
                jfif = true;
                int x = BinaryPrimitives.ReadUInt16BigEndian(segment[8..]);
                int y = BinaryPrimitives.ReadUInt16BigEndian(segment[10..]);
                if (x == 0 || y == 0)
                {
                    throw new InvalidDataException("JPEG JFIF pixel densities must be nonzero.");
                }
                Aspect = new(y, x);
            }
            else if (marker == 238 && segment.StartsWith("Adobe"u8))
            {
                if (adobeTransform is not null || segment.Length != 12 || segment[11] > 2)
                {
                    throw new InvalidDataException("JPEG Adobe color transform is duplicated or invalid.");
                }
                adobeTransform = segment[11];
            }
            else if (marker == 226 && segment.StartsWith("ICC_PROFILE\0"u8))
            {
                if (segment.Length < 14 || segment[12] == 0 || segment[13] == 0 || segment[12] > segment[13] ||
                    (iccCount != 0 && iccCount != segment[13]) || iccSeen[segment[12]])
                {
                    throw new InvalidDataException("JPEG ICC profile part numbering is invalid.");
                }
                iccCount = segment[13];
                iccSeen[segment[12]] = true;
                iccParts++;
                iccFragments ??= new ReadOnlyMemory<byte>[iccCount];
                iccFragments[segment[12] - 1] = data[14..];
            }
            else if (marker == 225 && segment.StartsWith("Exif\0\0"u8))
            {
                if (exif) { throw new InvalidDataException("JPEG Exif metadata is duplicated."); }
                JpegExif metadata = new(segment[6..], allowUnalignedExifOffsets, token);
                metadata.Read();
                exif = true;
                Orientation = metadata.Orientation;
                Centered = metadata.Centered;
                exifColorSpace = metadata.ColorSpace;
                exifCustomColor = metadata.CustomColor;
                HasUnalignedExifOffsets = metadata.HasUnalignedOffsets;
            }
            else if (marker == 232 && segment.StartsWith("SPIFF\0"u8))
            {
                throw new NotSupportedException("JPEG SPIFF presentation metadata is not implemented yet.");
            }
        }

        internal IccColorDescription CreateIccDescription() => IccColorDescription.FromSegments(
            iccFragments ?? throw new InvalidOperationException("JPEG has no ICC profile."), false);

        internal void Validate(IReadOnlyList<JpegComponent> components, JpegColorSpace? sourceColorSpace,
            SampleAspectRatio? sampleAspectRatio, bool requireColor, JpegChromaConflictPolicy chromaConflictPolicy)
        {
            if (iccParts != iccCount)
            {
                throw new InvalidDataException("JPEG ICC profile is missing one or more parts.");
            }
            if (exif)
            {
                if (Aspect is SampleAspectRatio ratio && ratio.Numerator != ratio.Denominator)
                {
                    throw new InvalidDataException("JPEG JFIF density conflicts with Exif square pixels.");
                }
                Aspect = SampleAspectRatio.Square;
                if (jfif && !Centered)
                {
                    switch (chromaConflictPolicy)
                    {
                        case JpegChromaConflictPolicy.Reject:
                            throw new InvalidDataException("JPEG JFIF and Exif chroma positions conflict.");
                        case JpegChromaConflictPolicy.UseJfif:
                            Centered = true;
                            UsedJfifChromaPosition = true;
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(chromaConflictPolicy));
                    }
                }
                if (exifCustomColor && requireColor && !HasIcc) { throw new NotSupportedException("JPEG custom Exif colorimetry is not supported."); }
            }
            if (sampleAspectRatio is SampleAspectRatio suppliedAspect)
            {
                if (Aspect is SampleAspectRatio declaredAspect &&
                    (long)declaredAspect.Numerator * suppliedAspect.Denominator != (long)suppliedAspect.Numerator * declaredAspect.Denominator)
                {
                    throw new InvalidDataException("JPEG pixel aspect ratio conflicts with the supplied interpretation.");
                }
                Aspect = suppliedAspect;
            }
            if (jfif)
            {
                if (components.Count == 4)
                {
                    throw new InvalidDataException("JFIF does not declare four-component JPEG color.");
                }
                for (int i = 0; i < components.Count; i++)
                {
                    if (components[i].Id != i + 1)
                    {
                        throw new InvalidDataException("JPEG JFIF component identifiers are invalid.");
                    }
                }
                if (components.Count == 3 && adobeTransform is not null and not 1)
                {
                    throw new InvalidDataException("JPEG JFIF and Adobe color declarations conflict.");
                }
            }
            if (components.Count == 1)
            {
                if (sourceColorSpace is not null)
                {
                    throw new InvalidDataException("A three-component JPEG color interpretation cannot be applied to grayscale.");
                }
                if (adobeTransform is not null and not 0)
                {
                    throw new InvalidDataException("JPEG grayscale conflicts with its Adobe transform.");
                }
                return;
            }
            if (components.Count == 4)
            {
                JpegColorSpace? fourComponentColor = adobeTransform switch
                {
                    0 => JpegColorSpace.InvertedCmyk,
                    2 => JpegColorSpace.Ycck,
                    1 => throw new InvalidDataException("A four-component JPEG cannot declare the Adobe YCbCr transform."),
                    null when components[0].Id == 'C' && components[1].Id == 'M' &&
                        components[2].Id == 'Y' && components[3].Id == 'K' => JpegColorSpace.Cmyk,
                    null => null,
                    _ => throw new InvalidDataException("Invalid JPEG Adobe color transform.")
                };
                if (sourceColorSpace is JpegColorSpace.Rgb or JpegColorSpace.YCbCr ||
                    fourComponentColor is not null && sourceColorSpace is not null && fourComponentColor != sourceColorSpace)
                {
                    throw new InvalidDataException("JPEG component count or declaration conflicts with the supplied color interpretation.");
                }
                if (fourComponentColor is null && sourceColorSpace is null && requireColor)
                {
                    throw new NotSupportedException("Four-component JPEG color requires a CMYK declaration or caller interpretation.");
                }
                ColorSpace = fourComponentColor ?? sourceColorSpace ?? JpegColorSpace.Cmyk;
                return;
            }
            if (adobeTransform == 2 || sourceColorSpace is JpegColorSpace.Cmyk or JpegColorSpace.InvertedCmyk or JpegColorSpace.Ycck)
            {
                throw new InvalidDataException("Four-component JPEG color cannot be applied to three components.");
            }
            if (!jfif && !exif && adobeTransform is null && sourceColorSpace is null)
            {
                if (requireColor)
                {
                    throw new NotSupportedException("JPEG three-component color requires an explicit JFIF or Adobe declaration or caller interpretation.");
                }
                return;
            }
            JpegColorSpace? declared = null;
            if (jfif || exif || adobeTransform == 1)
            {
                if (exif && adobeTransform is not null and not 1) { throw new InvalidDataException("JPEG Exif and Adobe color declarations conflict."); }
                declared = JpegColorSpace.YCbCr;
            }
            else if (adobeTransform == 0)
            {
                declared = JpegColorSpace.Rgb;
            }
            if (declared is not null && sourceColorSpace is not null && declared != sourceColorSpace)
            {
                throw new InvalidDataException("JPEG color declaration conflicts with the supplied interpretation.");
            }
            ColorSpace = (declared ?? sourceColorSpace) switch
            {
                JpegColorSpace.Rgb => JpegColorSpace.Rgb,
                JpegColorSpace.YCbCr => JpegColorSpace.YCbCr,
                _ => throw new InvalidDataException("JPEG component color interpretation is missing or invalid.")
            };
        }
    }
}

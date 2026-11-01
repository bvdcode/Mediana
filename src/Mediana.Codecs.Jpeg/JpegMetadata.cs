// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core.Video;

namespace Mediana.Codecs.Jpeg
{
    internal class JpegMetadata
    {
        private bool jfif;
        private int? adobeTransform;
        private int iccCount;
        private int iccParts;
        private readonly bool[] iccSeen = new bool[256];
        internal bool IsRgb { get; private set; }
        internal bool HasIcc => iccCount != 0;
        internal SampleAspectRatio? Aspect { get; private set; }

        internal void Read(byte marker, ReadOnlySpan<byte> segment)
        {
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
            }
            else if (marker == 225 && segment.StartsWith("Exif\0\0"u8))
            {
                throw new NotSupportedException("JPEG Exif presentation and color metadata are not implemented yet.");
            }
            else if (marker == 232 && segment.StartsWith("SPIFF\0"u8))
            {
                throw new NotSupportedException("JPEG SPIFF presentation metadata is not implemented yet.");
            }
        }

        internal void Validate(IReadOnlyList<JpegComponent> components, JpegColorSpace? sourceColorSpace,
            SampleAspectRatio? sampleAspectRatio, bool requireColor)
        {
            if (iccParts != iccCount)
            {
                throw new InvalidDataException("JPEG ICC profile is missing one or more parts.");
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
            if (adobeTransform == 2)
            {
                throw new NotSupportedException("JPEG YCCK color conversion is not supported.");
            }
            if (!jfif && adobeTransform is null && sourceColorSpace is null)
            {
                if (requireColor)
                {
                    throw new NotSupportedException("JPEG three-component color requires an explicit JFIF or Adobe declaration or caller interpretation.");
                }
                return;
            }
            JpegColorSpace? declared = null;
            if (jfif || adobeTransform == 1)
            {
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
            IsRgb = (declared ?? sourceColorSpace) switch
            {
                JpegColorSpace.Rgb => true,
                JpegColorSpace.YCbCr => false,
                _ => throw new InvalidDataException("JPEG component color interpretation is missing or invalid.")
            };
        }
    }
}

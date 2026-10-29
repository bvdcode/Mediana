// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Mediana.Images.Heif.Binary;
using Mediana.Images.Heif.Hevc;

namespace Mediana.Images.Heif.Container
{
    internal class ImageProperties
    {
        private readonly List<HeifBox> transforms = new List<HeifBox>();

        public int Width { get; private set; }
        public int Height { get; private set; }
        public DecoderConfiguration? Configuration { get; private set; }
        public ColorEncoding? Color { get; private set; }
        public ReadOnlyMemory<byte> IccProfile { get; private set; }
        public ReadOnlyMemory<byte> PixelDepths { get; private set; }
        public bool PixelDepthsEssential { get; private set; }
        public string? AuxiliaryType { get; private set; }
        public ReadOnlyMemory<byte> AuxiliarySubtype { get; private set; }
        public AlphaChannelInformation? AlphaInformation { get; private set; }
        public IReadOnlyList<HeifBox> Transforms => transforms;

        public static ImageProperties Read(HeifContainer container, HeifItem item, HeicDecodeOptions options)
        {
            ImageProperties result = new ImageProperties();
            HashSet<uint> seen = new HashSet<uint>();
            foreach (PropertyAssociation association in item.Associations)
            {
                HeifBox property = container.Properties[association.Index];
                bool color = property.Type == HeifBox.Code("colr");
                if (!color && !seen.Add(property.Type))
                {
                    throw new InvalidDataException("Duplicate image property.");
                }

                result.Read(property, association.Essential, options);
            }

            if ((result.Width == 0) || (result.Height == 0))
            {
                throw new InvalidDataException("Image spatial extents are missing.");
            }

            if ((result.AuxiliaryType == "urn:mpeg:hevc:2015:auxid:1") && !result.AuxiliarySubtype.IsEmpty)
            {
                DecoderConfiguration configuration = result.Configuration ??
                    throw new InvalidDataException("HEVC auxiliary configuration is missing.");
                result.AlphaInformation = HevcAuxiliaryInformation.Read(result.AuxiliarySubtype, configuration.LengthSize);
            }

            return result;
        }

        private void Read(HeifBox box, bool essential, HeicDecodeOptions options)
        {
            ByteReader reader = new ByteReader(box.Payload);
            if (box.Type == HeifBox.Code("ispe"))
            {
                FullBox(reader);
                uint width = reader.U32();
                uint height = reader.U32();
                if ((width == 0) || (height == 0) || (((ulong)width * height) > (ulong)options.MaxPixels))
                {
                    throw new InvalidDataException("Image dimensions exceed the pixel limit.");
                }

                Width = checked((int)width);
                Height = checked((int)height);
            }
            else if (box.Type == HeifBox.Code("hvcC"))
            {
                Configuration = DecoderConfiguration.Read(reader.Bytes(reader.Remaining));
            }
            else if (box.Type == HeifBox.Code("colr"))
            {
                ReadColor(reader);
            }
            else if (box.Type == HeifBox.Code("pixi"))
            {
                FullBox(reader);
                PixelDepths = reader.Bytes(reader.U8());
                PixelDepthsEssential = essential;
            }
            else if (box.Type == HeifBox.Code("auxC"))
            {
                ReadAuxiliary(reader);
            }
            else if ((box.Type == HeifBox.Code("irot")) || (box.Type == HeifBox.Code("imir")) || (box.Type == HeifBox.Code("clap")))
            {
                transforms.Add(box);
                reader.Skip(reader.Remaining);
            }
            else if (essential)
            {
                throw new UnsupportedHeicFeatureException("essential image property " + box.Type.ToString("X8"));
            }
            else
            {
                reader.Skip(reader.Remaining);
            }

            if (reader.Remaining != 0)
            {
                throw new InvalidDataException("Unexpected image property data.");
            }
        }

        private void ReadAuxiliary(ByteReader reader)
        {
            FullBox(reader);
            ReadOnlyMemory<byte> bytes = reader.Bytes(reader.Remaining);
            int end = bytes.Span.IndexOf((byte)0);
            if (end < 0)
            {
                throw new InvalidDataException("Auxiliary type is not null terminated.");
            }

            AuxiliaryType = Encoding.UTF8.GetString(bytes.Span.Slice(0, end));
            AuxiliarySubtype = bytes.Slice(end + 1);
            if ((AuxiliaryType == "urn:mpeg:mpegB:cicp:systems:auxiliary:alpha") && !AuxiliarySubtype.IsEmpty)
            {
                throw new UnsupportedHeicFeatureException("MPEG alpha auxiliary subtype");
            }
        }

        private void ReadColor(ByteReader reader)
        {
            uint type = reader.U32();
            if (type == HeifBox.Code("nclx"))
            {
                if (Color.HasValue)
                {
                    throw new InvalidDataException("Duplicate NCLX colour information.");
                }

                int primaries = reader.U16();
                int transfer = reader.U16();
                int matrix = reader.U16();
                byte range = reader.U8();
                if ((range & 127) != 0)
                {
                    throw new InvalidDataException("Nonzero NCLX reserved bits.");
                }

                Color = new ColorEncoding(primaries, transfer, matrix, (range & 128) != 0);
            }
            else if ((type == HeifBox.Code("prof")) || (type == HeifBox.Code("rICC")))
            {
                if (!IccProfile.IsEmpty)
                {
                    throw new InvalidDataException("Duplicate ICC colour profile.");
                }

                IccProfile = reader.Bytes(reader.Remaining);
            }
            else
            {
                throw new UnsupportedHeicFeatureException("colour information type " + type.ToString("X8"));
            }
        }

        public static bool IsAlpha(string? auxiliaryType) => (auxiliaryType == "urn:mpeg:hevc:2015:auxid:1") ||
            (auxiliaryType == "urn:mpeg:mpegB:cicp:systems:auxiliary:alpha");

        private static void FullBox(ByteReader reader)
        {
            if (reader.U32() != 0)
            {
                throw new UnsupportedHeicFeatureException("image property version or flags");
            }
        }
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO;

namespace Mediana.Images.Heif.Hevc
{
    /// <summary>
    /// Holds the explicitly signalled video colour information from HEVC Annex E.
    /// </summary>
    internal class VideoInformation
    {
        private const int ExtendedSampleAspectRatio = 255;
        private const int SampleAspectRatioComponentBits = 16;
        public int ColourPrimaries { get; private set; } = 2;
        public int TransferCharacteristics { get; private set; } = 2;
        public int MatrixCoefficients { get; private set; } = 2;
        public bool FullRange { get; private set; }
        public int ChromaLocation { get; private set; }

        public static VideoInformation Read(BitReader reader, int maxSubLayer)
        {
            VideoInformation information = new VideoInformation();
            ReadAspectRatio(reader);

            if (reader.ReadFlag())
            {
                reader.ReadFlag();
            }
            if (reader.ReadFlag())
            {
                reader.SkipBits(3);
                information.FullRange = reader.ReadFlag();
                if (reader.ReadFlag())
                {
                    information.ColourPrimaries = reader.Read(8);
                    information.TransferCharacteristics = reader.Read(8);
                    information.MatrixCoefficients = reader.Read(8);
                }
            }

            if (reader.ReadFlag())
            {
                information.ChromaLocation = HevcSyntax.Ue(reader, 5, "chroma_sample_loc_type_top_field");
                HevcSyntax.Ue(reader, 5, "chroma_sample_loc_type_bottom_field");
            }

            reader.ReadFlag();
            if (reader.ReadFlag())
            {
                throw new UnsupportedHeicFeatureException("HEVC field sequences");
            }
            reader.ReadFlag();
            if (reader.ReadFlag())
            {
                for (int i = 0; i < 4; i++)
                {
                    if (reader.ReadUev() != 0)
                    {
                        throw new UnsupportedHeicFeatureException("VUI default display window");
                    }
                }
            }

            if (reader.ReadFlag())
            {
                reader.SkipBits(64);
                if (reader.ReadFlag())
                {
                    reader.ReadUev();
                }
                if (reader.ReadFlag())
                {
                    HrdParameters.Skip(reader, maxSubLayer);
                }
            }

            if (reader.ReadFlag())
            {
                reader.SkipBits(3);
                HevcSyntax.Ue(reader, 4095, "min_spatial_segmentation_idc");
                HevcSyntax.Ue(reader, 16, "max_bytes_per_pic_denom");
                HevcSyntax.Ue(reader, 16, "max_bits_per_min_cu_denom");
                HevcSyntax.Ue(reader, 15, "log2_max_mv_length_horizontal");
                HevcSyntax.Ue(reader, 15, "log2_max_mv_length_vertical");
            }

            return information;
        }

        private static void ReadAspectRatio(BitReader reader)
        {
            if (reader.ReadFlag())
            {
                int aspectRatioIdc = reader.Read(8);
                if (aspectRatioIdc == ExtendedSampleAspectRatio)
                {
                    int sampleAspectRatioWidth = reader.Read(SampleAspectRatioComponentBits);
                    int sampleAspectRatioHeight = reader.Read(SampleAspectRatioComponentBits);
                    if ((sampleAspectRatioWidth == 0) || (sampleAspectRatioHeight == 0))
                    {
                        throw new InvalidDataException("Extended sample aspect ratio components must be nonzero.");
                    }
                }
            }
        }
    }
}

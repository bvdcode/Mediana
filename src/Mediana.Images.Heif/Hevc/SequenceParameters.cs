// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.IO;

namespace Mediana.Images.Heif.Hevc
{
    /// <summary>
    /// Owns one immutable parsed SPS and its scaling and colour parameters.
    /// </summary>
    /// <remarks>
    /// Width/Height are coded luma dimensions without CTB padding. CropLeft/CropTop are
    /// luma sample offsets; VisibleWidth/VisibleHeight exclude the conformance window.
    /// Bit depth is shared by all present components and is restricted to 8 or 10.
    /// </remarks>
    internal class SequenceParameters
    {
        public int Id { get; private set; }
        public int Profile { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int CropLeft { get; private set; }
        public int CropTop { get; private set; }
        public int VisibleWidth { get; private set; }
        public int VisibleHeight { get; private set; }
        public int BitDepth { get; private set; }
        public int ChromaFormat { get; private set; }
        public int SubWidthC => ChromaFormat switch
        {
            0 => 1,
            1 => 2,
            2 => 2,
            3 => 1,
            _ => throw new InvalidOperationException("Invalid chroma format.")
        };
        public int SubHeightC => ChromaFormat switch
        {
            0 => 1,
            1 => 2,
            2 => 1,
            3 => 1,
            _ => throw new InvalidOperationException("Invalid chroma format.")
        };
        public int Log2MinCodingBlock { get; private set; }
        public int Log2Ctb { get; private set; }
        public int Log2MinTransform { get; private set; }
        public int Log2MaxTransform { get; private set; }
        public int MaxTransformDepthIntra { get; private set; }
        public bool SaoEnabled { get; private set; }
        public ReferencePictureSyntax ReferencePictures { get; private set; } = null!;
        public bool TemporalMvpEnabled { get; private set; }
        public bool StrongIntraSmoothing { get; private set; }
        public ScalingLists Scaling { get; private set; } = ScalingLists.Flat;
        public VideoInformation Video { get; private set; } = new VideoInformation();

        public static SequenceParameters Read(NalPayload nal, HeicDecodeOptions options)
        {
            BitReader reader = HevcSyntax.Open(nal, 33);
            reader.Read(4);
            int subLayers = reader.Read(3);
            if (subLayers > 6)
            {
                throw new InvalidDataException("Invalid HEVC sub-layer count.");
            }
            reader.ReadFlag();
            SequenceParameters sps = new SequenceParameters { Profile = HevcSyntax.ProfileTierLevel(reader, subLayers) };
            sps.Id = HevcSyntax.Ue(reader, 15, "sps_seq_parameter_set_id");
            sps.ChromaFormat = HevcSyntax.Ue(reader, 3, "chroma_format_idc");
            if ((sps.ChromaFormat == 3) && reader.ReadFlag())
            {
                throw new UnsupportedHeicFeatureException("separate HEVC colour planes");
            }
            sps.ReadDimensions(reader, options);
            sps.BitDepth = HevcSyntax.Ue(reader, 8, "bit_depth_luma_minus8") + 8;
            int chromaDepth = HevcSyntax.Ue(reader, 8, "bit_depth_chroma_minus8") + 8;
            if ((sps.BitDepth != chromaDepth) || ((sps.BitDepth != 8) && (sps.BitDepth != 10)))
            {
                throw new UnsupportedHeicFeatureException("HEVC bit depths other than equal 8/10-bit components");
            }

            int log2Poc = HevcSyntax.Ue(reader, 12, "log2_max_pic_order_cnt_lsb_minus4") + 4;
            bool allOrdering = reader.ReadFlag();
            for (int i = allOrdering ? 0 : subLayers; i <= subLayers; i++)
            {
                int buffering = HevcSyntax.Ue(reader, 15, "sps_max_dec_pic_buffering_minus1");
                HevcSyntax.Ue(reader, buffering, "sps_max_num_reorder_pics");
                reader.ReadUev();
            }

            sps.ReadBlockSizes(reader);
            if (reader.ReadFlag())
            {
                sps.Scaling = reader.ReadFlag() ? ScalingLists.Read(reader) : ScalingLists.Default;
            }
            reader.ReadFlag();
            sps.SaoEnabled = reader.ReadFlag();
            if (reader.ReadFlag())
            {
                throw new UnsupportedHeicFeatureException("HEVC PCM coding");
            }
            sps.ReferencePictures = ReferencePictureSyntax.Read(reader, log2Poc);
            sps.TemporalMvpEnabled = reader.ReadFlag();
            sps.StrongIntraSmoothing = reader.ReadFlag();
            if (reader.ReadFlag())
            {
                sps.Video = VideoInformation.Read(reader, subLayers);
            }
            HevcSyntax.SequenceExtensions(reader);
            HevcSyntax.TrailingBits(reader);
            return sps;
        }

        private void ReadDimensions(BitReader reader, HeicDecodeOptions options)
        {
            Width = HevcSyntax.Ue(reader, options.MaxPixels, "pic_width_in_luma_samples");
            Height = HevcSyntax.Ue(reader, options.MaxPixels, "pic_height_in_luma_samples");
            if ((Width == 0) || (Height == 0) || (((long)Width * Height) > options.MaxPixels))
            {
                throw new InvalidDataException("HEVC coded dimensions exceed the pixel limit.");
            }

            int right = 0;
            int bottom = 0;
            if (reader.ReadFlag())
            {
                CropLeft = HevcSyntax.Ue(reader, Width / SubWidthC, "conf_win_left_offset") * SubWidthC;
                right = HevcSyntax.Ue(reader, Width / SubWidthC, "conf_win_right_offset") * SubWidthC;
                CropTop = HevcSyntax.Ue(reader, Height / SubHeightC, "conf_win_top_offset") * SubHeightC;
                bottom = HevcSyntax.Ue(reader, Height / SubHeightC, "conf_win_bottom_offset") * SubHeightC;
            }

            VisibleWidth = Width - CropLeft - right;
            VisibleHeight = Height - CropTop - bottom;
            if ((VisibleWidth <= 0) || (VisibleHeight <= 0))
            {
                throw new InvalidDataException("Invalid HEVC conformance window.");
            }
        }

        private void ReadBlockSizes(BitReader reader)
        {
            Log2MinCodingBlock = HevcSyntax.Ue(reader, 3, "log2_min_luma_coding_block_size_minus3") + 3;
            Log2Ctb = Log2MinCodingBlock + HevcSyntax.Ue(reader, 6 - Log2MinCodingBlock, "log2_diff_max_min_luma_coding_block_size");
            Log2MinTransform = HevcSyntax.Ue(reader, 3, "log2_min_luma_transform_block_size_minus2") + 2;
            if ((Log2MinTransform >= Log2MinCodingBlock) || (Log2Ctb < 4))
            {
                throw new InvalidDataException("Invalid HEVC coding or transform block size.");
            }

            int maxTransform = System.Math.Min(Log2Ctb, 5);
            Log2MaxTransform = Log2MinTransform + HevcSyntax.Ue(reader, maxTransform - Log2MinTransform, "log2_diff_max_min_luma_transform_block_size");
            HevcSyntax.Ue(reader, Log2Ctb - Log2MinTransform, "max_transform_hierarchy_depth_inter");
            MaxTransformDepthIntra = HevcSyntax.Ue(reader, Log2Ctb - Log2MinTransform, "max_transform_hierarchy_depth_intra");
            int minimum = 1 << Log2MinCodingBlock;
            if (((Width % minimum) != 0) || ((Height % minimum) != 0))
            {
                throw new InvalidDataException("Coded dimensions must be multiples of the minimum coding block.");
            }
        }
    }
}

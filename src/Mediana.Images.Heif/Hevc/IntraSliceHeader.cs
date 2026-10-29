// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Collections.Generic;
using System.IO;

namespace Mediana.Images.Heif.Hevc
{
    /// <summary>
    /// Describes an independent intra random-access slice and positions its reader at the CABAC payload.
    /// </summary>
    internal class IntraSliceHeader
    {
        public PictureParameters Picture { get; private set; } = null!;
        public BitReader Reader { get; private set; } = null!;
        public IReadOnlyList<ArraySegment<byte>> RowSubstreams { get; private set; } = Array.Empty<ArraySegment<byte>>();
        public int PictureOrderCount { get; private set; }
        public int StartCtbAddress { get; private set; }
        public int EndCtbAddress { get; set; }
        public bool LoopFilterAcrossSlices { get; private set; }
        public int Qp { get; private set; }
        public int CbQpOffset { get; private set; }
        public int CrQpOffset { get; private set; }
        public bool SaoLuma { get; private set; }
        public bool SaoChroma { get; private set; }
        public bool DeblockingDisabled { get; private set; }
        public int BetaOffset { get; private set; }
        public int TcOffset { get; private set; }

        public static IntraSliceHeader Read(NalPayload nal, IReadOnlyDictionary<int, PictureParameters> pictures)
        {
            if ((nal.Type < 16) || (nal.Type > 21))
            {
                throw new UnsupportedHeicFeatureException("HEVC pictures other than intra random access");
            }
            BitReader reader = new BitReader(nal.Bytes);
            reader.SkipBits(16);
            bool first = reader.ReadFlag();
            reader.ReadFlag();
            int id = HevcSyntax.Ue(reader, 63, "slice_pic_parameter_set_id");
            if (!pictures.TryGetValue(id, out PictureParameters? picture))
            {
                throw new InvalidDataException("Missing picture parameter set.");
            }
            int columns = ((picture.Sequence.Width - 1) / (1 << picture.Sequence.Log2Ctb)) + 1;
            int rows = ((picture.Sequence.Height - 1) / (1 << picture.Sequence.Log2Ctb)) + 1;
            int count = checked(columns * rows);
            int start = ReadStart(reader, picture, first, count);
            IntraSliceHeader slice = new IntraSliceHeader
            {
                Picture = picture,
                Reader = reader,
                StartCtbAddress = start,
                EndCtbAddress = count,
                LoopFilterAcrossSlices = picture.LoopFilterAcrossSlices,
                DeblockingDisabled = picture.DeblockingDisabled,
                BetaOffset = picture.BetaOffset,
                TcOffset = picture.TcOffset
            };
            slice.ReadPictureInformation(nal.Type);
            slice.ReadQuantization();
            slice.ReadFilters();
            slice.ReadEntryPoints(nal, rows);
            return slice;
        }

        private static int ReadStart(BitReader reader, PictureParameters picture, bool first, int count)
        {
            int start = 0;
            if (!first)
            {
                if (picture.DependentSlices && reader.ReadFlag())
                {
                    throw new UnsupportedHeicFeatureException("dependent HEVC slice segments");
                }
                int bits = 0;
                for (int value = count - 1; value > 0; value >>= 1)
                {
                    bits++;
                }
                start = reader.Read(bits);
                if ((start <= 0) || (start >= count))
                {
                    throw new InvalidDataException("Invalid slice segment address.");
                }
            }
            return start;
        }

        private void ReadPictureInformation(int nalType)
        {
            BitReader reader = Reader;
            PictureParameters picture = Picture;
            for (int i = 0; i < picture.ExtraHeaderBits; i++)
            {
                if (reader.ReadFlag())
                {
                    throw new UnsupportedHeicFeatureException("nonzero reserved slice header flags");
                }
            }

            if (HevcSyntax.Ue(reader, 2, "slice_type") != 2)
            {
                throw new UnsupportedHeicFeatureException("HEVC inter prediction");
            }
            if (picture.OutputFlagPresent && !reader.ReadFlag())
            {
                throw new UnsupportedHeicFeatureException("HEVC picture output suppression");
            }
            if ((nalType != 19) && (nalType != 20))
            {
                this.PictureOrderCount = picture.Sequence.ReferencePictures.ReadIntraSlice(reader);
                if (picture.Sequence.TemporalMvpEnabled)
                {
                    reader.ReadFlag();
                }
            }
            if (picture.Sequence.SaoEnabled)
            {
                this.SaoLuma = reader.ReadFlag();
                if (picture.Sequence.ChromaFormat != 0)
                {
                    this.SaoChroma = reader.ReadFlag();
                }
            }
        }

        private void ReadQuantization()
        {
            BitReader reader = Reader;
            PictureParameters picture = Picture;
            int depthOffset = 6 * (picture.Sequence.BitDepth - 8);
            this.Qp = picture.InitialQp + HevcSyntax.Se(reader, -51 - depthOffset, 51 + depthOffset, "slice_qp_delta");
            if ((this.Qp < -depthOffset) || (this.Qp > 51))
            {
                throw new InvalidDataException("Invalid slice luma QP.");
            }
            if (picture.SliceChromaQpOffsets)
            {
                this.CbQpOffset = HevcSyntax.Se(reader, -12, 12, "slice_cb_qp_offset");
                this.CrQpOffset = HevcSyntax.Se(reader, -12, 12, "slice_cr_qp_offset");
                if ((System.Math.Abs(this.CbQpOffset + picture.CbQpOffset) > 12) || (System.Math.Abs(this.CrQpOffset + picture.CrQpOffset) > 12))
                {
                    throw new InvalidDataException("Combined chroma QP offset is out of range.");
                }
            }
        }

        private void ReadFilters()
        {
            BitReader reader = Reader;
            PictureParameters picture = Picture;
            if (picture.DeblockingOverride && reader.ReadFlag())
            {
                this.DeblockingDisabled = reader.ReadFlag();
                if (!this.DeblockingDisabled)
                {
                    this.BetaOffset = 2 * HevcSyntax.Se(reader, -6, 6, "slice_beta_offset_div2");
                    this.TcOffset = 2 * HevcSyntax.Se(reader, -6, 6, "slice_tc_offset_div2");
                }
            }

            if (picture.LoopFilterAcrossSlices && (this.SaoLuma || this.SaoChroma || !this.DeblockingDisabled))
            {
                this.LoopFilterAcrossSlices = reader.ReadFlag();
            }
        }

        private void ReadEntryPoints(NalPayload nal, int rows)
        {
            BitReader reader = Reader;
            PictureParameters picture = Picture;
            int[] entryLengths = Array.Empty<int>();
            if (picture.EntropyCodingSync)
            {
                int entries = HevcSyntax.Ue(reader, rows - 1, "num_entry_point_offsets");
                entryLengths = new int[entries];
                if (entries > 0)
                {
                    int bits = HevcSyntax.Ue(reader, 31, "offset_len_minus1") + 1;
                    for (int i = 0; i < entries; i++)
                    {
                        uint offset = unchecked((uint)reader.Read(bits));
                        if (offset >= int.MaxValue)
                        {
                            throw new InvalidDataException("HEVC entry point offset exceeds the NAL payload.");
                        }
                        entryLengths[i] = (int)offset + 1;
                    }
                }
            }
            if (picture.HeaderExtension)
            {
                int length = HevcSyntax.Ue(reader, reader.BitsRemaining / 8, "slice_segment_header_extension_length");
                reader.SkipBits(length * 8);
            }

            HevcSyntax.Alignment(reader);
            if (picture.EntropyCodingSync)
            {
                this.RowSubstreams = nal.Substreams(checked((int)(reader.GetBitPosition() / 8)), entryLengths);
                this.Reader = new BitReader(this.RowSubstreams[0]);
            }
        }
    }
}

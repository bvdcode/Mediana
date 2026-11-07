// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Collections.Generic;
using System.IO;

namespace Mediana.Codecs.Hevc
{
    /// <summary>
    /// Describes an independent HEVC slice and positions its reader at the CABAC payload.
    /// </summary>
    internal class HevcSliceHeader
    {
        public HevcSliceType Type { get; private set; }
        public int NalType { get; private set; }
        public int TemporalId { get; private set; }
        public bool NoOutputOfPriorPictures { get; private set; }
        public bool PictureOutput { get; private set; } = true;
        public bool TemporalMvp { get; private set; }
        public int ActiveReferences0 { get; private set; }
        public int ActiveReferences1 { get; private set; }
        public IReadOnlyList<int> List0Entries { get; private set; } = Array.Empty<int>();
        public IReadOnlyList<int> List1Entries { get; private set; } = Array.Empty<int>();
        public bool MvdList1Zero { get; private set; }
        public bool CabacInit { get; private set; }
        public bool CollocatedFromList0 { get; private set; } = true;
        public int CollocatedReference { get; private set; }
        public int MaximumMergeCandidates { get; private set; }
        public HevcPredictionWeights? Weights { get; private set; }
        public int EntropyDataOffset { get; private set; }
        public int CabacInitializationType => Type switch
        {
            HevcSliceType.I => 0,
            HevcSliceType.P => CabacInit ? 2 : 1,
            HevcSliceType.B => CabacInit ? 1 : 2,
            _ => throw new InvalidDataException("Invalid HEVC slice type.")
        };
        public PictureParameters Picture { get; private set; } = null!;
        public BitReader Reader { get; private set; } = null!;
        public IReadOnlyList<ArraySegment<byte>> RowSubstreams { get; private set; } = Array.Empty<ArraySegment<byte>>();
        public int PictureOrderCount { get; private set; }
        public HevcSliceReferences References { get; private set; } = HevcSliceReferences.Empty;
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

        public static HevcSliceHeader Read(NalPayload nal, IReadOnlyDictionary<int, PictureParameters> pictures)
        {
            if (nal.Type is not (>= 0 and <= 9 or >= 16 and <= 21))
            {
                throw new UnsupportedHevcFeatureException("reserved or non-slice HEVC NAL units");
            }
            BitReader reader = new BitReader(nal.Bytes);
            reader.SkipBits(16);
            bool first = reader.ReadFlag();
            bool noPrior = nal.Type is >= 16 and <= 21 && reader.ReadFlag();
            int id = HevcSyntax.Ue(reader, 63, "slice_pic_parameter_set_id");
            if (!pictures.TryGetValue(id, out PictureParameters? picture))
            {
                throw new InvalidDataException("Missing picture parameter set.");
            }
            int columns = ((picture.Sequence.Width - 1) / (1 << picture.Sequence.Log2Ctb)) + 1;
            int rows = ((picture.Sequence.Height - 1) / (1 << picture.Sequence.Log2Ctb)) + 1;
            int count = checked(columns * rows);
            int start = ReadStart(reader, picture, first, count);
            HevcSliceHeader slice = new HevcSliceHeader
            {
                Picture = picture,
                NalType = nal.Type,
                TemporalId = nal.TemporalId,
                NoOutputOfPriorPictures = noPrior,
                Reader = reader,
                StartCtbAddress = start,
                EndCtbAddress = count,
                LoopFilterAcrossSlices = picture.LoopFilterAcrossSlices,
                DeblockingDisabled = picture.DeblockingDisabled,
                BetaOffset = picture.BetaOffset,
                TcOffset = picture.TcOffset
            };
            slice.ReadPictureInformation(nal.Type);
            slice.ReadInterInformation();
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
                    throw new UnsupportedHevcFeatureException("dependent HEVC slice segments");
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
                    throw new UnsupportedHevcFeatureException("nonzero reserved slice header flags");
                }
            }

            Type = (HevcSliceType)HevcSyntax.Ue(reader, 2, "slice_type");
            if (nalType is >= 16 and <= 21 && Type != HevcSliceType.I)
            {
                throw new InvalidDataException("An HEVC random-access slice must use intra prediction.");
            }
            if (picture.OutputFlagPresent) { PictureOutput = reader.ReadFlag(); }
            if ((nalType != 19) && (nalType != 20))
            {
                References = picture.Sequence.ReferencePictures.ReadSlice(reader);
                this.PictureOrderCount = References.PictureOrderCountLsb;
                if (picture.Sequence.TemporalMvpEnabled)
                {
                    TemporalMvp = reader.ReadFlag();
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

        private void ReadInterInformation()
        {
            if (Type == HevcSliceType.I) { return; }
            if (References.UsedCount == 0) { throw new InvalidDataException("An HEVC inter slice has no current reference pictures."); }
            ActiveReferences0 = Picture.DefaultReferences0;
            ActiveReferences1 = Type == HevcSliceType.B ? Picture.DefaultReferences1 : 0;
            if (Reader.ReadFlag())
            {
                ActiveReferences0 = HevcSyntax.Ue(Reader, 14, "num_ref_idx_l0_active_minus1") + 1;
                if (Type == HevcSliceType.B) { ActiveReferences1 = HevcSyntax.Ue(Reader, 14, "num_ref_idx_l1_active_minus1") + 1; }
            }
            if (Picture.ListModificationPresent && References.UsedCount > 1)
            {
                List0Entries = ReadListModification(ActiveReferences0);
                if (Type == HevcSliceType.B) { List1Entries = ReadListModification(ActiveReferences1); }
            }
            if (Type == HevcSliceType.B) { MvdList1Zero = Reader.ReadFlag(); }
            if (Picture.CabacInitPresent) { CabacInit = Reader.ReadFlag(); }
            if (TemporalMvp)
            {
                if (Type == HevcSliceType.B) { CollocatedFromList0 = Reader.ReadFlag(); }
                int count = CollocatedFromList0 ? ActiveReferences0 : ActiveReferences1;
                if (count > 1) { CollocatedReference = HevcSyntax.Ue(Reader, count - 1, "collocated_ref_idx"); }
            }
            if ((Picture.WeightedPrediction && Type == HevcSliceType.P) ||
                (Picture.WeightedBiprediction && Type == HevcSliceType.B))
            {
                Weights = HevcPredictionWeights.Read(Reader, Picture.Sequence.ChromaFormat != 0, ActiveReferences0, ActiveReferences1);
            }
            MaximumMergeCandidates = 5 - HevcSyntax.Ue(Reader, 4, "five_minus_max_num_merge_cand");
        }

        private IReadOnlyList<int> ReadListModification(int count)
        {
            if (!Reader.ReadFlag()) { return Array.Empty<int>(); }
            int bits = 0;
            for (int value = References.UsedCount - 1; value > 0; value >>= 1) { bits++; }
            int[] entries = new int[count];
            for (int i = 0; i < count; i++)
            {
                int entry = Reader.Read(bits);
                if (entry >= References.UsedCount) { throw new InvalidDataException("An HEVC reference list entry exceeds its current pictures."); }
                entries[i] = entry;
            }
            return Array.AsReadOnly(entries);
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
            EntropyDataOffset = checked((int)(reader.GetBitPosition() / 8));
            if (picture.EntropyCodingSync)
            {
                this.RowSubstreams = nal.Substreams(checked((int)(reader.GetBitPosition() / 8)), entryLengths);
                this.Reader = new BitReader(this.RowSubstreams[0]);
            }
        }
    }
}

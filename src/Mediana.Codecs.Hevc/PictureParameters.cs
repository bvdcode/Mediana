// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Collections.Generic;
using System.IO;

namespace Mediana.Codecs.Hevc
{
    internal class PictureParameters
    {
        public int Id { get; private set; }
        public SequenceParameters Sequence { get; private set; } = null!;
        public bool DependentSlices { get; private set; }
        public bool OutputFlagPresent { get; private set; }
        public int ExtraHeaderBits { get; private set; }
        public bool SignDataHiding { get; private set; }
        public bool CabacInitPresent { get; private set; }
        public int DefaultReferences0 { get; private set; }
        public int DefaultReferences1 { get; private set; }
        public bool ConstrainedIntraPrediction { get; private set; }
        public bool WeightedPrediction { get; private set; }
        public bool WeightedBiprediction { get; private set; }
        public bool ListModificationPresent { get; private set; }
        public int Log2ParallelMergeLevel { get; private set; }
        public int InitialQp { get; private set; }
        public bool TransformSkip { get; private set; }
        public bool CuQpDelta { get; private set; }
        public int QpDeltaDepth { get; private set; }
        public int CbQpOffset { get; private set; }
        public int CrQpOffset { get; private set; }
        public bool SliceChromaQpOffsets { get; private set; }
        public bool TransquantBypass { get; private set; }
        public bool EntropyCodingSync { get; private set; }
        public HevcTileLayout Tiles { get; private set; } = null!;
        public bool LoopFilterAcrossTiles { get; private set; } = true;
        public bool LoopFilterAcrossSlices { get; private set; }
        public bool DeblockingOverride { get; private set; }
        public bool DeblockingDisabled { get; private set; }
        public int BetaOffset { get; private set; }
        public int TcOffset { get; private set; }
        public bool HeaderExtension { get; private set; }
        public ScalingLists Scaling { get; private set; } = ScalingLists.Flat;

        public static PictureParameters Read(NalPayload nal, IReadOnlyDictionary<int, SequenceParameters> sequences)
        {
            BitReader reader = HevcSyntax.Open(nal, 34);
            int id = HevcSyntax.Ue(reader, 63, "pps_pic_parameter_set_id");
            int sequenceId = HevcSyntax.Ue(reader, 15, "pps_seq_parameter_set_id");
            if (!sequences.TryGetValue(sequenceId, out SequenceParameters? sequence))
            {
                throw new InvalidDataException("PPS refers to a missing sequence parameter set.");
            }

            PictureParameters pps = new PictureParameters { Id = id, Sequence = sequence, Scaling = sequence.Scaling };
            pps.DependentSlices = reader.ReadFlag();
            pps.OutputFlagPresent = reader.ReadFlag();
            pps.ExtraHeaderBits = reader.Read(3);
            pps.SignDataHiding = reader.ReadFlag();
            pps.CabacInitPresent = reader.ReadFlag();
            pps.DefaultReferences0 = HevcSyntax.Ue(reader, 14, "num_ref_idx_l0_default_active_minus1") + 1;
            pps.DefaultReferences1 = HevcSyntax.Ue(reader, 14, "num_ref_idx_l1_default_active_minus1") + 1;
            pps.InitialQp = 26 + HevcSyntax.Se(reader, -26 - (6 * (sequence.BitDepth - 8)), 25, "init_qp_minus26");
            pps.ConstrainedIntraPrediction = reader.ReadFlag();
            pps.TransformSkip = reader.ReadFlag();
            pps.CuQpDelta = reader.ReadFlag();
            if (pps.CuQpDelta)
            {
                pps.QpDeltaDepth = HevcSyntax.Ue(reader, sequence.Log2Ctb - sequence.Log2MinCodingBlock, "diff_cu_qp_delta_depth");
            }
            pps.CbQpOffset = HevcSyntax.Se(reader, -12, 12, "pps_cb_qp_offset");
            pps.CrQpOffset = HevcSyntax.Se(reader, -12, 12, "pps_cr_qp_offset");
            pps.SliceChromaQpOffsets = reader.ReadFlag();
            pps.WeightedPrediction = reader.ReadFlag();
            pps.WeightedBiprediction = reader.ReadFlag();
            pps.TransquantBypass = reader.ReadFlag();
            bool tilesEnabled = reader.ReadFlag();
            pps.EntropyCodingSync = reader.ReadFlag();
            pps.Tiles = HevcTileLayout.Read(reader, sequence, tilesEnabled);
            if (tilesEnabled) { pps.LoopFilterAcrossTiles = reader.ReadFlag(); }
            pps.LoopFilterAcrossSlices = reader.ReadFlag();
            if (reader.ReadFlag())
            {
                pps.DeblockingOverride = reader.ReadFlag();
                pps.DeblockingDisabled = reader.ReadFlag();
                if (!pps.DeblockingDisabled)
                {
                    pps.BetaOffset = 2 * HevcSyntax.Se(reader, -6, 6, "pps_beta_offset_div2");
                    pps.TcOffset = 2 * HevcSyntax.Se(reader, -6, 6, "pps_tc_offset_div2");
                }
            }

            if (reader.ReadFlag())
            {
                pps.Scaling = ScalingLists.Read(reader);
            }
            pps.ListModificationPresent = reader.ReadFlag();
            pps.Log2ParallelMergeLevel = HevcSyntax.Ue(reader, sequence.Log2Ctb - 2, "log2_parallel_merge_level_minus2") + 2;
            pps.HeaderExtension = reader.ReadFlag();
            if (reader.ReadFlag() && (reader.Read(8) != 0))
            {
                throw new UnsupportedHevcFeatureException("HEVC picture extensions");
            }
            HevcSyntax.TrailingBits(reader);
            return pps;
        }
    }
}

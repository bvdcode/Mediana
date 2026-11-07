// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text.Json;
using Mediana.Codecs.Hevc;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Formats.Video;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class HevcSliceHeaderTestInput
    {
        public static async Task<int> VerifyAsync(string root, string name)
        {
            using JsonDocument reference = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, name + ".headers.json")));
            JsonElement[] expected = reference.RootElement.EnumerateArray().Where(value => value.GetProperty("kind").GetString() == "Slice Segment Header").ToArray();
            MediaBufferAllocator allocator = new(1024 * 1024);
            int count = 0;
            await using (RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, name + ".mp4"))))
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator))
            {
                HevcDecoderConfiguration configuration = HevcDecoderConfiguration.Read(reader.Streams[0].Configuration);
                Dictionary<int, SequenceParameters> sequences = [];
                Dictionary<int, PictureParameters> pictures = [];
                foreach (HevcNalArray array in configuration.Arrays)
                {
                    foreach (ReadOnlyMemory<byte> bytes in array.Units)
                    {
                        NalPayload nal = new(bytes);
                        if (nal.Type == 33) { SequenceParameters sequence = SequenceParameters.Read(nal, int.MaxValue); sequences.Add(sequence.Id, sequence); }
                        if (nal.Type == 34) { PictureParameters picture = PictureParameters.Read(nal, sequences); pictures.Add(picture.Id, picture); }
                    }
                }
                JsonElement pps = reference.RootElement.EnumerateArray().First(value => value.GetProperty("kind").GetString() == "Picture Parameter Set").GetProperty("fields");
                foreach (PictureParameters picture in pictures.Values)
                {
                    Equal(pps, "cabac_init_present_flag", picture.CabacInitPresent);
                    Equal(pps, "num_ref_idx_l0_default_active_minus1", picture.DefaultReferences0 - 1);
                    Equal(pps, "num_ref_idx_l1_default_active_minus1", picture.DefaultReferences1 - 1);
                    Equal(pps, "constrained_intra_pred_flag", picture.ConstrainedIntraPrediction);
                    Equal(pps, "weighted_pred_flag", picture.WeightedPrediction);
                    Equal(pps, "weighted_bipred_flag", picture.WeightedBiprediction);
                    Equal(pps, "lists_modification_present_flag", picture.ListModificationPresent);
                    Equal(pps, "log2_parallel_merge_level_minus2", picture.Log2ParallelMergeLevel - 2);
                }
                for (int packetIndex = 0; packetIndex < reader.GetPacketCount(0); packetIndex++)
                {
                    using MediaPacket packet = await reader.ReadPacketAtAsync(0, packetIndex);
                    foreach (NalPayload nal in NalPayload.ReadLengthPrefixed(packet.Data, configuration.LengthSize))
                    {
                        if (nal.Type is not (>= 0 and <= 9 or >= 16 and <= 21)) { continue; }
                        if (count >= expected.Length) { throw new InvalidDataException("The HEVC reference has fewer slice headers."); }
                        Verify(HevcSliceHeader.Read(nal, pictures), expected[count++]);
                    }
                }
            }
            if (count != expected.Length || allocator.AllocatedBytes != 0) { throw new InvalidDataException("The HEVC header count or retained allocation differs."); }
            return count;
        }

        private static void Verify(HevcSliceHeader actual, JsonElement section)
        {
            JsonElement fields = section.GetProperty("fields");
            Equal(fields, "nal_unit_type", actual.NalType);
            Equal(fields, "nuh_temporal_id_plus1", actual.TemporalId + 1);
            Equal(fields, "slice_type", (int)actual.Type);
            Equal(fields, "slice_pic_order_cnt_lsb", actual.PictureOrderCount);
            Equal(fields, "no_output_of_prior_pics_flag", actual.NoOutputOfPriorPictures);
            Equal(fields, "pic_output_flag", actual.PictureOutput, 1);
            Equal(fields, "slice_temporal_mvp_enabled_flag", actual.TemporalMvp);
            Equal(fields, "slice_sao_luma_flag", actual.SaoLuma);
            Equal(fields, "slice_sao_chroma_flag", actual.SaoChroma);
            Equal(fields, "slice_qp_delta", actual.Qp - actual.Picture.InitialQp);
            Equal(fields, "slice_cb_qp_offset", actual.CbQpOffset);
            Equal(fields, "slice_cr_qp_offset", actual.CrQpOffset);
            Equal(fields, "slice_loop_filter_across_slices_enabled_flag", actual.LoopFilterAcrossSlices,
                actual.Picture.LoopFilterAcrossSlices ? 1 : 0);
            if (section.GetProperty("endBit").GetInt32() != actual.EntropyDataOffset * 8)
            {
                throw new InvalidDataException("The HEVC entropy start differs from the independent header parser.");
            }
            if (actual.Type == HevcSliceType.I) { return; }
            Equal(fields, "num_ref_idx_l0_active_minus1", actual.ActiveReferences0 - 1, actual.Picture.DefaultReferences0 - 1);
            if (actual.Type == HevcSliceType.B)
            {
                Equal(fields, "num_ref_idx_l1_active_minus1", actual.ActiveReferences1 - 1, actual.Picture.DefaultReferences1 - 1);
            }
            Equal(fields, "mvd_l1_zero_flag", actual.MvdList1Zero);
            Equal(fields, "cabac_init_flag", actual.CabacInit);
            Equal(fields, "collocated_from_l0_flag", actual.CollocatedFromList0, 1);
            Equal(fields, "collocated_ref_idx", actual.CollocatedReference);
            Equal(fields, "five_minus_max_num_merge_cand", 5 - actual.MaximumMergeCandidates);
            if (actual.Weights is not null)
            {
                Equal(fields, "luma_log2_weight_denom", actual.Weights.LumaDenominator);
                Equal(fields, "delta_chroma_log2_weight_denom", actual.Weights.ChromaDenominator - actual.Weights.LumaDenominator);
                VerifyWeights(fields, actual.Weights, actual.Weights.List0, "l0");
                VerifyWeights(fields, actual.Weights, actual.Weights.List1, "l1");
            }
        }

        private static void VerifyWeights(JsonElement fields, HevcPredictionWeights weights,
            IReadOnlyList<HevcPredictionWeight> list, string suffix)
        {
            for (int i = 0; i < list.Count; i++)
            {
                HevcPredictionWeight weight = list[i];
                int luma = 1 << weights.LumaDenominator, chroma = 1 << weights.ChromaDenominator;
                Equal(fields, $"delta_luma_weight_{suffix}[{i}]", weight.LumaWeight - luma);
                Equal(fields, $"luma_offset_{suffix}[{i}]", weight.LumaOffset);
                Equal(fields, $"delta_chroma_weight_{suffix}[{i}][0]", weight.CbWeight - chroma);
                Equal(fields, $"delta_chroma_weight_{suffix}[{i}][1]", weight.CrWeight - chroma);
                int cb = Math.Clamp(128 + Read(fields, $"chroma_offset_{suffix}[{i}][0]") - ((128 * weight.CbWeight) >> weights.ChromaDenominator), -128, 127);
                int cr = Math.Clamp(128 + Read(fields, $"chroma_offset_{suffix}[{i}][1]") - ((128 * weight.CrWeight) >> weights.ChromaDenominator), -128, 127);
                if (weight.CbOffset != cb || weight.CrOffset != cr) { throw new InvalidDataException($"The HEVC chroma prediction offsets differ: {suffix}/{i} Cb {weight.CbOffset}/{cb}, Cr {weight.CrOffset}/{cr}, weights {weight.CbWeight}/{weight.CrWeight}, denominator {weights.ChromaDenominator}."); }
            }
        }

        private static int Read(JsonElement fields, string name, int inferred = 0) => fields.TryGetProperty(name, out JsonElement value) ? value.GetInt32() : inferred;
        private static void Equal(JsonElement fields, string name, bool actual, int inferred = 0) => Equal(fields, name, actual ? 1 : 0, inferred);
        private static void Equal(JsonElement fields, string name, int actual, int inferred = 0)
        {
            int expected = Read(fields, name, inferred);
            if (actual != expected) { throw new InvalidDataException($"HEVC {name}: {actual}, expected {expected}."); }
        }
    }
}

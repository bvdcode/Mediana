// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Vp9
{
    internal partial class Vp9FrameHeader
    {
        internal int FilterLevel { get; private set; }
        internal int FilterSharpness { get; private set; }
        internal bool FilterDeltas { get; private set; }
        internal int BaseQuantizer { get; private set; }
        internal int YDcDelta { get; private set; }
        internal int UvDcDelta { get; private set; }
        internal int UvAcDelta { get; private set; }
        internal bool Lossless => BaseQuantizer == 0 && YDcDelta == 0 && UvDcDelta == 0 && UvAcDelta == 0;
        internal bool Segmentation { get; private set; }
        internal bool UpdateSegmentMap { get; private set; }
        internal bool TemporalSegments { get; private set; }
        internal readonly byte[] SegmentTree = new byte[7], SegmentPrediction = new byte[3];
        internal int TileColumnsLog2 { get; private set; }
        internal int TileRowsLog2 { get; private set; }
        internal int MiColumns => (Geometry.Width + 7) / 8;
        internal int MiRows => (Geometry.Height + 7) / 8;
        internal int SuperblockColumns => (MiColumns + 7) / 8;
        internal int SuperblockRows => (MiRows + 7) / 8;

        private void ReadFilter(ref Vp9BitReader bits, Vp9HeaderState state)
        {
            FilterLevel = bits.Read(6); FilterSharpness = bits.Read(3); FilterDeltas = bits.Flag();
            if (!FilterDeltas || !bits.Flag()) { return; }
            for (int i = 0; i < 4; i++) { if (bits.Flag()) { state.ReferenceDeltas[i] = bits.Signed(6); } }
            for (int i = 0; i < 2; i++) { if (bits.Flag()) { state.ModeDeltas[i] = bits.Signed(6); } }
        }

        private void ReadQuantizers(ref Vp9BitReader bits)
        {
            BaseQuantizer = bits.Read(8); YDcDelta = ReadDelta(ref bits); UvDcDelta = ReadDelta(ref bits); UvAcDelta = ReadDelta(ref bits);
        }

        private static int ReadDelta(ref Vp9BitReader bits) => bits.Flag() ? bits.Signed(4) : 0;
        private static byte ReadProbability(ref Vp9BitReader bits) => bits.Flag() ? (byte)bits.Read(8) : (byte)255;

        private void ReadSegmentation(ref Vp9BitReader bits, Vp9HeaderState state)
        {
            SegmentTree.AsSpan().Fill(255); SegmentPrediction.AsSpan().Fill(255);
            Segmentation = bits.Flag();
            if (!Segmentation) { return; }
            UpdateSegmentMap = bits.Flag();
            if (UpdateSegmentMap)
            {
                for (int i = 0; i < SegmentTree.Length; i++) { SegmentTree[i] = ReadProbability(ref bits); }
                TemporalSegments = bits.Flag();
                if (TemporalSegments)
                { for (int i = 0; i < SegmentPrediction.Length; i++) { SegmentPrediction[i] = ReadProbability(ref bits); } }
            }
            if (!bits.Flag()) { return; }
            state.AbsoluteSegments = bits.Flag();
            ReadOnlySpan<int> featureBits = [8, 6, 2, 0];
            for (int segment = 0; segment < 8; segment++)
            {
                for (int feature = 0; feature < 4; feature++)
                {
                    int index = segment * 4 + feature; bool enabled = bits.Flag(); state.SegmentEnabled[index] = enabled;
                    int value = enabled ? bits.Read(featureBits[feature]) : 0;
                    if (enabled && feature < 2 && bits.Flag()) { value = -value; }
                    state.SegmentFeatures[index] = value;
                }
            }
        }

        private void ReadTiles(ref Vp9BitReader bits)
        {
            int min = 0, max = 1;
            while ((64 << min) < SuperblockColumns) { min++; }
            while ((SuperblockColumns >> max) >= 4) { max++; }
            max--;
            TileColumnsLog2 = min;
            while (TileColumnsLog2 < max && bits.Flag()) { TileColumnsLog2++; }
            TileRowsLog2 = bits.Read(1);
            if (TileRowsLog2 != 0) { TileRowsLog2 += bits.Read(1); }
        }
    }
}

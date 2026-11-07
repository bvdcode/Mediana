// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Vp8
{
    internal class Vp8FrameHeader
    {
        internal readonly Vp8BooleanReader Control;
        internal readonly Vp8BooleanReader[] Tokens;
        internal readonly byte[] Probabilities, LumaProbabilities, ChromaProbabilities, MotionProbabilities;
        internal readonly byte[] SegmentProbabilities = [255, 255, 255];
        internal readonly int[] Quantizers = new int[4], FilterLevels = new int[4], Deltas = new int[5];
        internal readonly bool Segmentation, UpdateMap, SimpleFilter, FilterDeltas, SkipEnabled;
        internal readonly int Version, Sharpness, IntraDelta, BlockDelta, SkipProbability;
        internal readonly bool KeyFrame, LoopFilterEnabled, RefreshEntropy, RefreshLast, RefreshGolden, RefreshAlternate;
        internal readonly int CopyGolden, CopyAlternate, IntraProbability, LastProbability, GoldenProbability;
        internal readonly int[] SignBias = new int[4];
        private readonly Vp8CodingState state;
        internal Vp8FrameHeader(ReadOnlyMemory<byte> encoded, CancellationToken token) : this(encoded, new Vp8CodingState(), token) { }
        internal Vp8FrameHeader(ReadOnlyMemory<byte> encoded, Vp8CodingState state, CancellationToken token)
        {
            Vp8UncompressedHeader uncompressed = Vp8UncompressedHeader.Parse(encoded.Span);
            KeyFrame = uncompressed.KeyFrame; Version = uncompressed.Version;
            this.state = state;
            if (KeyFrame) { state.Reset(); }
            Probabilities = state.Coefficients.ToArray();
            LumaProbabilities = state.Luma.ToArray(); ChromaProbabilities = state.Chroma.ToArray();
            MotionProbabilities = state.Motion.ToArray();
            int partitionLength = uncompressed.ControlLength;
            Control = new(encoded.Slice(uncompressed.ControlOffset, partitionLength));
            if (KeyFrame)
            {
                if (Control.Read()) { throw new NotSupportedException("VP8 reserved color space is not supported."); }
                Control.Read();
            }
            Segmentation = Control.Read();
            if (Segmentation)
            {
                UpdateMap = Control.Read();
                if (Control.Read())
                {
                    state.AbsoluteSegments = Control.Read();
                    for (int segment = 0; segment < 4; segment++) { state.SegmentQuantizers[segment] = Control.Read() ? Control.Signed(7) : 0; }
                    for (int segment = 0; segment < 4; segment++) { state.SegmentFilters[segment] = Control.Read() ? Control.Signed(6) : 0; }
                }
                if (UpdateMap)
                { for (int index = 0; index < 3; index++) { if (Control.Read()) { SegmentProbabilities[index] = (byte)Control.Literal(8); } } }
            }
            SimpleFilter = Control.Read(); int filter = Control.Literal(6); LoopFilterEnabled = filter != 0; Sharpness = Control.Literal(3);
            FilterDeltas = Control.Read();
            int[] referenceDeltas = state.ReferenceDeltas, modeDeltas = state.ModeDeltas;
            if (FilterDeltas && Control.Read())
            {
                for (int index = 0; index < 4; index++) { if (Control.Read()) { referenceDeltas[index] = Control.Signed(6); } }
                for (int index = 0; index < 4; index++) { if (Control.Read()) { modeDeltas[index] = Control.Signed(6); } }
            }
            IntraDelta = referenceDeltas[0]; BlockDelta = modeDeltas[0];
            int partitions = 1 << Control.Literal(2), quantizer = Control.Literal(7);
            for (int index = 0; index < 5; index++) { if (Control.Read()) { Deltas[index] = Control.Signed(4); } }
            for (int segment = 0; segment < 4; segment++)
            {
                Quantizers[segment] = Math.Clamp(Segmentation ? state.AbsoluteSegments ? state.SegmentQuantizers[segment] : quantizer + state.SegmentQuantizers[segment] : quantizer, 0, 127);
                FilterLevels[segment] = Math.Clamp(Segmentation ? state.AbsoluteSegments ? state.SegmentFilters[segment] : filter + state.SegmentFilters[segment] : filter, 0, 63);
            }
            RefreshGolden = KeyFrame || Control.Read(); RefreshAlternate = KeyFrame || Control.Read();
            if (!RefreshGolden) { CopyGolden = Control.Literal(2); }
            if (!RefreshAlternate) { CopyAlternate = Control.Literal(2); }
            if (CopyGolden == 3 || CopyAlternate == 3) { throw new InvalidDataException("Reserved VP8 reference copy operation."); }
            if (!KeyFrame) { SignBias[2] = Control.Read() ? 1 : 0; SignBias[3] = Control.Read() ? 1 : 0; }
            RefreshEntropy = Control.Read(); RefreshLast = KeyFrame || Control.Read();
            ReadOnlySpan<byte> updates = Vp8ProbabilityTables.CoefficientUpdates;
            for (int index = 0; index < Probabilities.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                if (Control.Read(updates[index])) { Probabilities[index] = (byte)Control.Literal(8); }
            }
            SkipEnabled = Control.Read(); SkipProbability = SkipEnabled ? Control.Literal(8) : 0;
            if (!KeyFrame)
            {
                IntraProbability = Control.Literal(8); LastProbability = Control.Literal(8); GoldenProbability = Control.Literal(8);
                if (Control.Read()) { for (int i = 0; i < 4; i++) { LumaProbabilities[i] = (byte)Control.Literal(8); } }
                if (Control.Read()) { for (int i = 0; i < 3; i++) { ChromaProbabilities[i] = (byte)Control.Literal(8); } }
                ReadOnlySpan<byte> motionUpdates =
                [237, 246, 253, 253, 254, 254, 254, 254, 254, 254, 254, 254, 254, 254, 250, 250, 252, 254, 254,
                 231, 243, 245, 253, 254, 254, 254, 254, 254, 254, 254, 254, 254, 254, 251, 251, 254, 254, 254];
                for (int i = 0; i < MotionProbabilities.Length; i++)
                {
                    if (Control.Read(motionUpdates[i])) { int value = Control.Literal(7); MotionProbabilities[i] = (byte)(value == 0 ? 1 : value * 2); }
                }
            }
            int offset = uncompressed.ControlOffset + partitionLength, table = checked((partitions - 1) * 3), cursor = checked(offset + table);
            if (cursor > encoded.Length) { throw new InvalidDataException("Truncated VP8 partition size table."); }
            Tokens = new Vp8BooleanReader[partitions];
            for (int index = 0; index < partitions; index++)
            {
                int size = index == partitions - 1 ? encoded.Length - cursor : encoded.Span[offset + index * 3] | encoded.Span[offset + index * 3 + 1] << 8 | encoded.Span[offset + index * 3 + 2] << 16;
                if (size > encoded.Length - cursor) { throw new InvalidDataException("VP8 token partition exceeds its input."); }
                Tokens[index] = new(encoded.Slice(cursor, size)); cursor += size;
            }
        }
        internal void CommitEntropy()
        {
            if (!RefreshEntropy) { return; }
            Probabilities.CopyTo(state.Coefficients, 0); LumaProbabilities.CopyTo(state.Luma, 0);
            ChromaProbabilities.CopyTo(state.Chroma, 0); MotionProbabilities.CopyTo(state.Motion, 0);
        }
        internal int InterFilter(int segment, int reference, int mode)
        {
            int delta = mode switch { 0 => state.ModeDeltas[1], 1 or 2 or 3 => state.ModeDeltas[2], 4 => state.ModeDeltas[3], _ => throw new InvalidDataException("Invalid VP8 motion mode.") };
            return Math.Clamp(FilterLevels[segment] + (FilterDeltas ? state.ReferenceDeltas[reference] + delta : 0), 0, 63);
        }
        internal int Filter(int segment, bool blocks) => Math.Clamp(FilterLevels[segment] + (FilterDeltas ? IntraDelta + (blocks ? BlockDelta : 0) : 0), 0, 63);
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Webp
{
    internal class Vp8FrameHeader
    {
        internal readonly Vp8BooleanReader Control;
        internal readonly Vp8BooleanReader[] Tokens;
        internal readonly byte[] Probabilities = Vp8ProbabilityTables.Coefficients.ToArray();
        internal readonly byte[] SegmentProbabilities = [255, 255, 255];
        internal readonly int[] Quantizers = new int[4], FilterLevels = new int[4], Deltas = new int[5];
        internal readonly bool Segmentation, UpdateMap, SimpleFilter, FilterDeltas, SkipEnabled;
        internal readonly int Sharpness, IntraDelta, BlockDelta, SkipProbability;
        internal Vp8FrameHeader(ReadOnlyMemory<byte> encoded, CancellationToken token)
        {
            int partitionLength = (encoded.Span[0] | encoded.Span[1] << 8 | encoded.Span[2] << 16) >> 5;
            if (((encoded.Span[0] >> 1) & 7) > 3 || partitionLength > encoded.Length - 10)
            { throw new InvalidDataException("Invalid VP8 keyframe version or control partition extent."); }
            Control = new(encoded.Slice(10, partitionLength));
            if (Control.Read()) { throw new NotSupportedException("VP8 reserved color space is not supported."); }
            Control.Read();
            Segmentation = Control.Read(); bool absolute = false;
            if (Segmentation)
            {
                UpdateMap = Control.Read();
                if (Control.Read())
                {
                    absolute = Control.Read();
                    for (int segment = 0; segment < 4; segment++) { Quantizers[segment] = Control.Read() ? Control.Signed(7) : 0; }
                    for (int segment = 0; segment < 4; segment++) { FilterLevels[segment] = Control.Read() ? Control.Signed(6) : 0; }
                }
                if (UpdateMap)
                { for (int index = 0; index < 3; index++) { if (Control.Read()) { SegmentProbabilities[index] = (byte)Control.Literal(8); } } }
            }
            SimpleFilter = Control.Read(); int filter = Control.Literal(6); Sharpness = Control.Literal(3);
            FilterDeltas = Control.Read();
            Span<int> referenceDeltas = stackalloc int[] { 2, 0, -2, -2 }, modeDeltas = stackalloc int[] { 4, -2, 2, 4 };
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
                Quantizers[segment] = Math.Clamp(Segmentation && absolute ? Quantizers[segment] : quantizer + Quantizers[segment], 0, 127);
                FilterLevels[segment] = Math.Clamp(Segmentation && absolute ? FilterLevels[segment] : filter + FilterLevels[segment], 0, 63);
            }
            Control.Read();
            ReadOnlySpan<byte> updates = Vp8ProbabilityTables.CoefficientUpdates;
            for (int index = 0; index < Probabilities.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                if (Control.Read(updates[index])) { Probabilities[index] = (byte)Control.Literal(8); }
            }
            SkipEnabled = Control.Read(); SkipProbability = SkipEnabled ? Control.Literal(8) : 0;
            int offset = 10 + partitionLength, table = checked((partitions - 1) * 3), cursor = checked(offset + table);
            if (cursor > encoded.Length) { throw new InvalidDataException("Truncated VP8 partition size table."); }
            Tokens = new Vp8BooleanReader[partitions];
            for (int index = 0; index < partitions; index++)
            {
                int size = index == partitions - 1 ? encoded.Length - cursor : WebpDocument.Read24(encoded.Span[(offset + index * 3)..]);
                if (size > encoded.Length - cursor) { throw new InvalidDataException("VP8 token partition exceeds its input."); }
                Tokens[index] = new(encoded.Slice(cursor, size)); cursor += size;
            }
        }
        internal int Filter(int segment, bool blocks) => Math.Clamp(FilterLevels[segment] + (FilterDeltas ? IntraDelta + (blocks ? BlockDelta : 0) : 0), 0, 63);
    }
}

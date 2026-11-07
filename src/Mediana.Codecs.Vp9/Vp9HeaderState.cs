// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Vp9
{
    internal class Vp9HeaderState
    {
        internal readonly Vp9PictureGeometry?[] References = new Vp9PictureGeometry?[8];
        internal readonly int[] ReferenceDeltas = [1, 0, -1, -1], ModeDeltas = new int[2];
        internal readonly int[] SegmentFeatures = new int[32];
        internal readonly bool[] SegmentEnabled = new bool[32];
        internal bool AbsoluteSegments;

        internal void ResetIndependence()
        {
            ReadOnlySpan<int> deltas = [1, 0, -1, -1]; deltas.CopyTo(ReferenceDeltas);
            Array.Clear(ModeDeltas); Array.Clear(SegmentFeatures); Array.Clear(SegmentEnabled); AbsoluteSegments = false;
        }

        internal Vp9PictureGeometry Reference(int index) => References[index] ?? throw new InvalidDataException("A VP9 reference slot has not been initialized.");

        internal void Commit(Vp9FrameHeader header)
        {
            if (header.ShowExisting) { return; }
            for (int i = 0; i < 8; i++) { if ((header.RefreshReferences & (1 << i)) != 0) { References[i] = header.Geometry; } }
        }
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly record struct Av1Segmentation(bool Enabled, bool UpdateMap, bool TemporalUpdate, Av1Segments Segments)
    {
        internal bool PreSkip
        {
            get
            {
                for (int index = 0; index < 8; index++) { if (Segments[index].PreSkip) { return true; } }
                return false;
            }
        }

        internal int LastActiveId
        {
            get
            {
                for (int index = 7; index > 0; index--) { if (Segments[index].Mask != 0) { return index; } }
                return 0;
            }
        }

        internal bool CodedLossless(Av1Quantization quantization)
        {
            for (int index = 0; index < 8; index++)
            {
                if (!(quantization with { BaseIndex = Segments[index].QuantizerIndex(quantization.BaseIndex) }).Lossless) { return false; }
            }
            return true;
        }

        internal static Av1Segmentation Read(ref Av1BitReader bits, bool noPrimaryReference, Av1Segmentation previous)
        {
            if (!bits.Flag()) { return default; }
            bool updateMap = noPrimaryReference || bits.Flag();
            bool temporalUpdate = !noPrimaryReference && updateMap && bits.Flag();
            bool updateData = noPrimaryReference || bits.Flag();
            Av1Segments segments = previous.Segments;
            if (updateData)
            {
                for (int index = 0; index < 8; index++) { segments[index] = Av1SegmentFeatures.Read(ref bits); }
            }
            return new(true, updateMap, temporalUpdate, segments);
        }
    }
}

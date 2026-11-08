// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly record struct Av1LoopFilterParameters(int Vertical, int Horizontal, int U, int V,
        int Sharpness, bool DeltaEnabled, Av1LoopFilterDeltas ReferenceDeltas, int ModeZero, int ModeOne)
    {
        internal bool Enabled => Vertical != 0 || Horizontal != 0;
        internal static Av1LoopFilterParameters Default
        {
            get
            {
                Av1LoopFilterDeltas deltas = default;
                deltas[0] = 1;
                deltas[4] = deltas[6] = deltas[7] = -1;
                return new(0, 0, 0, 0, 0, false, deltas, 0, 0);
            }
        }

        internal int Level(int plane, int pass, Av1FilterBlock block, Av1Segmentation segmentation = default)
        {
            int level = plane switch
            {
                0 => pass switch { 0 => Vertical, 1 => Horizontal, _ => throw new ArgumentOutOfRangeException(nameof(pass)) },
                1 => U,
                2 => V,
                _ => throw new ArgumentOutOfRangeException(nameof(plane))
            };
            int index = plane switch
            {
                0 => pass,
                1 => 2,
                2 => 3,
                _ => throw new ArgumentOutOfRangeException(nameof(plane))
            };
            level = Math.Clamp(level + block.Deltas[index], 0, 63);
            level = Math.Clamp(level + segmentation.Segments[block.SegmentId].FilterDelta(index), 0, 63);
            if (DeltaEnabled)
            {
                int shift = level >> 5;
                level += ReferenceDeltas[block.Reference] << shift;
                if (block.Reference != 0) { level += (block.MotionMode ? ModeOne : ModeZero) << shift; }
                level = Math.Clamp(level, 0, 63);
            }
            return level;
        }

        internal static Av1LoopFilterParameters Read(ref Av1BitReader bits, bool monochrome, Av1LoopFilterParameters previous)
        {
            int vertical = bits.Read(6);
            int horizontal = bits.Read(6);
            int u = 0;
            int v = 0;
            if (!monochrome && (vertical != 0 || horizontal != 0))
            {
                u = bits.Read(6);
                v = bits.Read(6);
            }
            int sharpness = bits.Read(3);
            bool enabled = bits.Flag();
            Av1LoopFilterDeltas deltas = previous.ReferenceDeltas;
            int modeZero = previous.ModeZero;
            int modeOne = previous.ModeOne;
            if (enabled && bits.Flag())
            {
                for (int index = 0; index < 8; index++)
                {
                    if (bits.Flag()) { deltas[index] = (sbyte)bits.ReadSigned(7); }
                }
                if (bits.Flag()) { modeZero = bits.ReadSigned(7); }
                if (bits.Flag()) { modeOne = bits.ReadSigned(7); }
            }
            return new(vertical, horizontal, u, v, sharpness, enabled, deltas, modeZero, modeOne);
        }
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Video
{
    internal readonly record struct H264PictureLayout
    {
        public H264PictureLayout(H264SequenceParameterSet sequence, H264PictureStructure structure)
        {
            IsField = structure switch
            {
                H264PictureStructure.Frame => false,
                H264PictureStructure.TopField or H264PictureStructure.BottomField => true,
                _ => throw new ArgumentOutOfRangeException(nameof(structure))
            };
            if (IsField && sequence.FrameMbsOnly)
            {
                throw new InvalidDataException("A frame-only H.264 sequence cannot contain a field picture.");
            }
            MacroblockAdaptiveFrameField = sequence.MbAdaptiveFrameField && !IsField;
            int height = checked(sequence.HeightInMapUnits * (sequence.FrameMbsOnly || IsField ? 1 : 2));
            MacroblockCount = checked(sequence.WidthInMbs * height);
            MaximumPictureNumber = (1 << sequence.FrameNumberBits) * (IsField ? 2 : 1);
        }

        public bool IsField { get; }
        public bool MacroblockAdaptiveFrameField { get; }
        public int MacroblockCount { get; }
        public int MaximumPictureNumber { get; }

        public int FirstMacroblockAddress(int firstMacroblock)
        {
            int scale = MacroblockAdaptiveFrameField ? 2 : 1;
            if (firstMacroblock < 0 || firstMacroblock >= MacroblockCount / scale)
            {
                throw new InvalidDataException("The H.264 first macroblock is outside the picture.");
            }
            return firstMacroblock * scale;
        }
    }
}

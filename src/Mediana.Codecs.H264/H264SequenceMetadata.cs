// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Metadata;

namespace Mediana.Codecs.H264
{
    /// <summary>One opening SPS, with native identifiers, inferred syntax defaults and raw VUI color/timing codes.</summary>
    public class H264SequenceMetadata : VideoStreamConfiguration
    {
        internal H264SequenceMetadata(H264SequenceParameterSet sequence)
            : base(sequence.Width, sequence.Height, Sampling(sequence.ChromaFormat), sequence.LumaBitDepth,
                sequence.ChromaFormat == 0 ? null : sequence.ChromaBitDepth,
                sequence.FrameMbsOnly ? true : null, H264VideoFormat.ReadSampleAspectRatio(sequence.Vui),
                H264VideoFormat.ReadColor(sequence.Vui, CommonChromaLocation(sequence)))
        {
            SequenceId = sequence.Id;
            ProfileIdc = sequence.Profile;
            ConstraintFlags = sequence.Constraints;
            LevelIdc = sequence.Level;
            CodedWidth = sequence.CodedWidth;
            CodedHeight = sequence.CodedHeight;
            CropLeft = sequence.CropLeft;
            CropTop = sequence.CropTop;
            FrameMbsOnly = sequence.FrameMbsOnly;
            MbAdaptiveFrameField = sequence.MbAdaptiveFrameField;
            SeparateColourPlane = sequence.SeparateColourPlane;
            H264VuiParameters? vui = sequence.Vui;
            ColorPrimariesCode = vui?.ColorPrimaries ?? 2;
            TransferCharacteristicsCode = vui?.TransferCharacteristics ?? 2;
            MatrixCoefficientsCode = vui?.MatrixCoefficients ?? 2;
            TopFieldChromaLocation = vui?.ChromaLocationTop;
            BottomFieldChromaLocation = vui?.ChromaLocationBottom;
            UnitsInTick = vui?.UnitsInTick;
            TimeScale = vui?.TimeScale;
            if (UnitsInTick is not null)
            {
                FixedFrameRate = vui!.FixedFrameRate;
            }
        }

        public int SequenceId { get; }
        public int ProfileIdc { get; }
        public int ConstraintFlags { get; }
        public int LevelIdc { get; }
        public int CodedWidth { get; }
        public int CodedHeight { get; }
        public int CropLeft { get; }
        public int CropTop { get; }
        /// <summary>False permits coded frames or fields; it does not establish display field order.</summary>
        public bool FrameMbsOnly { get; }
        public bool MbAdaptiveFrameField { get; }
        public bool SeparateColourPlane { get; }
        public int ColorPrimariesCode { get; }
        public int TransferCharacteristicsCode { get; }
        public int MatrixCoefficientsCode { get; }
        /// <summary>Null means absent syntax; H.264 infers zero. Values are not applied outside 4:2:0.</summary>
        public int? TopFieldChromaLocation { get; }
        public int? BottomFieldChromaLocation { get; }
        /// <summary>Native VUI clock declarations, not an average frame rate or a measured duration.</summary>
        public uint? UnitsInTick { get; }
        public uint? TimeScale { get; }
        public bool? FixedFrameRate { get; }

        private static VideoChromaSampling Sampling(int code) => code switch
        {
            0 => VideoChromaSampling.Monochrome,
            1 => VideoChromaSampling.Sampling420,
            2 => VideoChromaSampling.Sampling422,
            3 => VideoChromaSampling.Sampling444,
            _ => throw new InvalidDataException("The H.264 chroma format is invalid.")
        };

        private static int? CommonChromaLocation(H264SequenceParameterSet sequence)
        {
            if (sequence.ChromaFormat != 1 || !sequence.FrameMbsOnly)
            {
                return null;
            }
            int top = sequence.Vui?.ChromaLocationTop ?? 0;
            int bottom = sequence.Vui?.ChromaLocationBottom ?? 0;
            if (top != bottom)
            {
                return null;
            }
            return top;
        }
    }
}

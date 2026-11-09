// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    public record H264EncoderOptions
    {
        /// <summary>Base QP, from 0 through 51. Macroblocks may increase QP to satisfy Baseline syntax and bit limits.</summary>
        public int Quantizer { get; init; } = 26;
        /// <summary>CAVLC uses Baseline profile; CABAC uses Main profile without changing prediction or quantization.</summary>
        public H264EntropyCoding EntropyCoding { get; init; } = H264EntropyCoding.Cavlc;
        /// <summary>Prediction analysis policy. Rate-distortion analysis requires CABAC; quality, compression and cost depend on the content.</summary>
        public H264MacroblockAnalysis MacroblockAnalysis { get; init; } = H264MacroblockAnalysis.Fast;
        /// <summary>In-loop deblocking settings. Null disables filtering.</summary>
        public H264DeblockingOptions? Deblocking { get; init; }
        public int MaximumPacketBytes { get; init; } = 16 * 1024 * 1024;
        /// <summary>Media-time IDR interval; the first eligible frame starts the next group. Zero makes every frame an IDR.</summary>
        public TimeSpan KeyFrameInterval { get; init; } = TimeSpan.FromSeconds(2);
        /// <summary>Maximum absolute motion displacement in luma pixels, from zero to 64. Zero disables motion refinement.</summary>
        public int MotionSearchRange { get; init; } = 16;
        public H264RateControlOptions? RateControl { get; init; }

        internal void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfNegative(Quantizer);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(Quantizer, 51);
            switch (EntropyCoding)
            {
                case H264EntropyCoding.Cavlc:
                case H264EntropyCoding.Cabac:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(EntropyCoding));
            }
            switch (MacroblockAnalysis)
            {
                case H264MacroblockAnalysis.Fast:
                    break;
                case H264MacroblockAnalysis.RateDistortion:
                    if (EntropyCoding != H264EntropyCoding.Cabac)
                    {
                        throw new NotSupportedException("Rate-distortion macroblock analysis requires CABAC entropy coding.");
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(MacroblockAnalysis));
            }
            ArgumentOutOfRangeException.ThrowIfLessThan(MaximumPacketBytes, 8);
            ArgumentOutOfRangeException.ThrowIfLessThan(KeyFrameInterval, TimeSpan.Zero);
            ArgumentOutOfRangeException.ThrowIfNegative(MotionSearchRange);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(MotionSearchRange, 64);
            RateControl?.Validate();
            Deblocking?.Validate();
        }
    }
}

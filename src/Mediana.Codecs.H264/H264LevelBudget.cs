// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Codecs.H264
{
    /// <summary>Level 4.1 access-unit and VCL/NAL buffer bounds in the supplied decode clock.</summary>
    internal class H264LevelBudget(TimeBase timeBase, int macroblocks, int parameterSetBytes)
    {
        private const int PrefixBytes = sizeof(uint);
        private const int DelimiterBytes = 2;
        private readonly H264BitCredit vcl = new(H264EncoderConfiguration.MaximumBitsPerSecond,
            H264EncoderConfiguration.MaximumCpbBits, timeBase);
        private readonly H264BitCredit nal = new(H264EncoderConfiguration.MaximumNalBitsPerSecond,
            H264EncoderConfiguration.MaximumNalCpbBits, timeBase);
        private long? previous;

        public int MaximumPacketBytes(long timestamp, bool keyFrame)
        {
            Int128 pictureBytes = MaximumAccessUnitBytes(timestamp, keyFrame) - AccessUnitOverhead(keyFrame) + PrefixBytes;
            Int128 vclBytes = vcl.Available(timestamp) / timeBase.Denominator / 8 + PrefixBytes;
            Int128 nalBytes = nal.Available(timestamp) / timeBase.Denominator / 8 - AnnexBOverhead(keyFrame);
            return (int)Int128.Clamp(Int128.Min(pictureBytes, Int128.Min(vclBytes, nalBytes)), 0, int.MaxValue);
        }

        public void Commit(long timestamp, bool keyFrame, int packetBytes)
        {
            if (packetBytes <= PrefixBytes || packetBytes > MaximumPacketBytes(timestamp, keyFrame))
            {
                throw new InvalidOperationException("The H.264 access unit exceeds its level 4.1 size or buffer budget.");
            }
            int nalBytes = checked(packetBytes + AnnexBOverhead(keyFrame));
            vcl.Commit(timestamp, packetBytes - PrefixBytes);
            nal.Commit(timestamp, nalBytes);
            previous = timestamp;
        }

        public void Reset()
        {
            vcl.Reset();
            nal.Reset();
            previous = null;
        }

        private Int128 MaximumAccessUnitBytes(long timestamp, bool keyFrame)
        {
            if (previous is not long last || keyFrame)
            {
                long pictureLimit = Math.Max((long)macroblocks * H264EncoderConfiguration.MaximumFramesPerSecond,
                    H264EncoderConfiguration.MaximumMacroblocksPerSecond);
                return (Int128)384 * pictureLimit /
                    (H264EncoderConfiguration.MaximumFramesPerSecond * H264EncoderConfiguration.MinimumCompressionRatio);
            }
            Int128 elapsed = (Int128)timestamp - last;
            return 384 * H264EncoderConfiguration.MaximumMacroblocksPerSecond * elapsed * timeBase.Numerator /
                ((Int128)H264EncoderConfiguration.MinimumCompressionRatio * timeBase.Denominator);
        }

        private int AccessUnitOverhead(bool keyFrame) => DelimiterBytes + (keyFrame ? parameterSetBytes : 0);

        private int AnnexBOverhead(bool keyFrame) => PrefixBytes + DelimiterBytes +
            (keyFrame ? parameterSetBytes + 2 * PrefixBytes : 0);
    }
}

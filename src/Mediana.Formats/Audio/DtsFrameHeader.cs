// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Audio
{
    /// <summary>DTS Core header facts from ETSI TS 102 114, independent of PCM reconstruction.</summary>
    public class DtsFrameHeader
    {
        public const string CodecId = "dts";
        public const int MaximumHeaderBytes = 20;
        public int FrameBytes { get; }
        public int SampleBlocks { get; }
        public int SampleRate { get; }
        public int CodedSamples => SampleBlocks * 32;
        public int SamplesPerFrame => CodedSamples + (IsTermination ? DeficitSamples : 0);
        public int PrimaryChannelCount => ChannelMode switch
        {
            0 => 1,
            1 or 2 or 3 or 4 => 2,
            5 or 6 => 3,
            7 or 8 => 4,
            9 => 5,
            _ => throw new NotSupportedException("The DTS channel arrangement requires an extended presentation.")
        };
        public int ChannelCount => PrimaryChannelCount + (LfeMode == 0 ? 0 : 1);
        public int ChannelMode { get; }
        public int LfeMode { get; }
        public int BitDepth { get; }
        public int BitRateCode { get; }
        public bool HasCrc { get; }
        public bool HasDynamicRange { get; }
        public bool HasTimeCode { get; }
        public bool HasAuxiliaryData { get; }
        public bool HasExtensions { get; }
        public int ExtensionType { get; }
        public bool SyncEachSubsubframe { get; }
        public bool PredictFromPreviousFrame { get; }
        public bool PerfectReconstruction { get; }
        public bool FrontSumDifference { get; }
        public bool SurroundSumDifference { get; }
        public int DialogNormalizationDb { get; }
        public int HeaderBits { get; }
        public bool IsTermination { get; }
        public int DeficitSamples { get; }

        private DtsFrameHeader(ReadOnlySpan<byte> data)
        {
            DtsBitReader bits = new(data);
            bits.Skip(32);
            IsTermination = bits.Read(1) == 0;
            DeficitSamples = bits.Read(5) + 1;
            HasCrc = bits.Read(1) != 0;
            SampleBlocks = bits.Read(7) + 1;
            int bytes = bits.Read(14) + 1;
            FrameBytes = bits.WordBits == 14 ? (bytes * 8 + 13) / 14 * 2 : bytes;
            ChannelMode = bits.Read(6);
            SampleRate = bits.Read(4) switch
            {
                1 => 8000, 2 => 16000, 3 => 32000, 6 => 11025, 7 => 22050,
                8 => 44100, 11 => 12000, 12 => 24000, 13 => 48000,
                _ => throw new InvalidDataException("The DTS Core sample rate code is reserved.")
            };
            BitRateCode = bits.Read(5);
            if (bits.Read(1) != 0) { throw new InvalidDataException("The DTS fixed header bit must be zero."); }
            HasDynamicRange = bits.Read(1) != 0;
            HasTimeCode = bits.Read(1) != 0;
            HasAuxiliaryData = bits.Read(1) != 0;
            bits.Skip(1);
            ExtensionType = bits.Read(3);
            HasExtensions = bits.Read(1) != 0;
            SyncEachSubsubframe = bits.Read(1) != 0;
            LfeMode = bits.Read(2);
            PredictFromPreviousFrame = bits.Read(1) != 0;
            if (HasCrc) { bits.Skip(16); }
            PerfectReconstruction = bits.Read(1) != 0;
            int version = bits.Read(4);
            bits.Skip(2);
            BitDepth = bits.Read(3) switch
            {
                0 or 1 => 16, 2 or 3 => 20, 5 or 6 => 24,
                _ => throw new InvalidDataException("The DTS source precision is reserved.")
            };
            FrontSumDifference = bits.Read(1) != 0 || ChannelMode == 3;
            SurroundSumDifference = bits.Read(1) != 0;
            int normalization = bits.Read(4);
            DialogNormalizationDb = version switch { 6 => -16 - normalization, 7 => -normalization, _ => 0 };
            HeaderBits = bits.Position;
            if (bytes < 96 || SampleBlocks < 6 || LfeMode == 3 || (IsTermination && DeficitSamples == 32) ||
                (!IsTermination && (DeficitSamples != 32 || SampleBlocks % 8 != 0)))
            { throw new InvalidDataException("The DTS frame extent, block count or LFE mode is invalid."); }
            if (ChannelMode > 9) { throw new NotSupportedException("This DTS channel arrangement requires an extended presentation."); }
            if ((FrontSumDifference && PrimaryChannelCount < 2) || (SurroundSumDifference && ChannelMode is not (8 or 9)))
            { throw new InvalidDataException("DTS sum/difference coding requires the corresponding channel pair."); }
        }

        public string[] GetChannelOrder()
        {
            string[] primary = ChannelMode switch
            {
                0 => ["front-center"],
                1 => ["discrete-0", "discrete-1"],
                2 or 3 or 4 => ["front-left", "front-right"],
                5 => ["front-center", "front-left", "front-right"],
                6 => ["front-left", "front-right", "back-center"],
                7 => ["front-center", "front-left", "front-right", "back-center"],
                8 => ["front-left", "front-right", "side-left", "side-right"],
                9 => ["front-center", "front-left", "front-right", "side-left", "side-right"],
                _ => throw new NotSupportedException("The DTS channel arrangement requires an extended presentation.")
            };
            return LfeMode == 0 ? primary : [.. primary, "low-frequency"];
        }

        public static DtsFrameHeader Read(ReadOnlySpan<byte> data) => new(data);
    }
}

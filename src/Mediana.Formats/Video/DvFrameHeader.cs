// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Formats.Video
{
    /// <summary>Video properties of an SD DV25 DIF frame.</summary>
    public readonly record struct DvFrameHeader(SampleAspectRatio SampleAspectRatio, VideoScanMode ScanMode)
    {
        public const string CodecId = "dvvideo";
        public const int Width = 720;
        public const int Height = 576;
        public const int DifBlockBytes = 80;
        public const int SequenceBlocks = 150;
        public const int SequenceCount = 12;
        public const int FrameBytes = DifBlockBytes * SequenceBlocks * SequenceCount;
        public const int NtscHeight = 480;
        public const int NtscSequenceCount = 10;
        public const int PrefixBytes = 6 * DifBlockBytes;
        public bool IsNtsc { get; private init; }
        public int CodedHeight => IsNtsc ? NtscHeight : Height;
        public int DifSequences => IsNtsc ? NtscSequenceCount : SequenceCount;

        public static DvFrameHeader Read(ReadOnlySpan<byte> data)
        {
            DvFrameHeader header = ReadPrefix(data);
            if (data.Length != DifBlockBytes * SequenceBlocks * header.DifSequences)
            {
                throw new InvalidDataException("A DV25 packet must contain exactly one complete DIF frame.");
            }
            return header;
        }

        public static DvFrameHeader ReadPrefix(ReadOnlySpan<byte> data)
        {
            if (data.Length < DifBlockBytes || (data[0] >> 5) != 0 || (data[1] >> 4) != 0 || data[2] != 0)
            {
                throw new InvalidDataException("A DV frame must start with the first DIF sequence header.");
            }
            bool ntsc = (data[3] & 0x80) == 0;
            int applicationId = data[4] & 7;
            if (!ntsc && applicationId != 0)
            {
                throw new NotSupportedException("Only 4:2:0 PAL DV25 and 4:1:1 NTSC DV25 are supported.");
            }
            if (data.Length < PrefixBytes)
            {
                throw new InvalidDataException("The DV video auxiliary data is truncated.");
            }

            for (int block = 3; block < 6; block++)
            {
                int start = block * DifBlockBytes;
                if ((data[start] >> 5) != 2 || (data[start + 1] >> 4) != 0 || data[start + 2] != block - 3)
                {
                    throw new InvalidDataException("A DV video auxiliary DIF block is out of sequence.");
                }
            }
            int source = FindPack(data, 0x60);
            if (source < 0)
            {
                throw new InvalidDataException("The DV frame is missing its video source pack.");
            }
            if ((data[source + 3] & 0x1f) != 0)
            {
                throw new NotSupportedException("The DV video source pack does not declare the DV25 profile.");
            }
            int control = FindPack(data, 0x61);
            if (control < 0)
            {
                throw new InvalidDataException("The DV frame is missing its video source control pack.");
            }

            int display = data[control + 2] & 7;
            bool widescreen = display == 2 || (applicationId == 0 && display == 7);
            SampleAspectRatio aspect = (ntsc, widescreen) switch
            {
                (true, true) => new(32, 27),
                (true, false) => new(8, 9),
                (false, true) => new(64, 45),
                (false, false) => new(16, 15)
            };
            byte flags = data[control + 3];
            VideoScanMode scanMode = (flags & 0x10) == 0 ? VideoScanMode.Progressive
                : (flags & 0x40) == 0 ? VideoScanMode.TopFieldFirst : VideoScanMode.BottomFieldFirst;
            return new(aspect, scanMode) { IsNtsc = ntsc };
        }

        private static int FindPack(ReadOnlySpan<byte> data, byte type)
        {
            for (int block = 3; block < 6; block++)
            {
                int start = block * DifBlockBytes + 3;
                for (int offset = 0; offset < 75; offset += 5)
                {
                    if (data[start + offset] == type)
                    {
                        return start + offset;
                    }
                }
            }
            return -1;
        }
    }
}

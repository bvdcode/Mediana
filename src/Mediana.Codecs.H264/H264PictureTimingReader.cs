// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264PictureTimingReader
    {
        public static H264PictureTiming Read(ref H264BitReader bits, int payloadBytes, H264VuiParameters? vui)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(payloadBytes);
            if (!bits.ByteAligned)
            {
                throw new InvalidDataException("An H.264 picture-timing payload must start on a byte boundary.");
            }
            long end = bits.BitPosition + (long)payloadBytes * 8;
            H264HrdTiming? hrd = vui?.NalHrdTiming ?? vui?.VclHrdTiming;
            if (hrd is H264HrdTiming timing)
            {
                ReadBits(ref bits, timing.RemovalDelayBits, end);
                ReadBits(ref bits, timing.OutputDelayBits, end);
            }
            H264DisplayStructure? structure = null;
            H264SourceScanTypes scans = H264SourceScanTypes.None;
            if (vui?.PictureStructurePresent == true)
            {
                structure = ReadStructure(ReadBits(ref bits, 4, end));
                int clocks = ClockCount(structure.Value);
                for (int index = 0; index < clocks; index++)
                {
                    if (ReadBits(ref bits, 1, end) != 0)
                    {
                        scans |= ReadClock(ref bits, hrd?.TimeOffsetBits ?? 24, end);
                    }
                }
            }
            if (!bits.ByteAligned)
            {
                if (ReadBits(ref bits, 1, end) != 1)
                {
                    throw new InvalidDataException("The H.264 picture-timing alignment marker is missing.");
                }
                while (!bits.ByteAligned)
                {
                    if (ReadBits(ref bits, 1, end) != 0)
                    {
                        throw new InvalidDataException("The H.264 picture-timing alignment contains nonzero bits.");
                    }
                }
            }
            if (bits.BitPosition != end)
            {
                throw new InvalidDataException("The H.264 picture-timing payload has unexpected trailing data.");
            }
            return new(structure, scans);
        }

        private static H264SourceScanTypes ReadClock(ref H264BitReader bits, int offsetBits, long end)
        {
            H264SourceScanTypes scan = ReadBits(ref bits, 2, end) switch
            {
                0 => H264SourceScanTypes.Progressive,
                1 => H264SourceScanTypes.Interlaced,
                2 => H264SourceScanTypes.Unknown,
                _ => throw new InvalidDataException("The H.264 clock timestamp has a reserved source scan type.")
            };
            ReadBits(ref bits, 1, end);
            ReadBounded(ref bits, 5, 6, end);
            bool full = ReadBits(ref bits, 1, end) != 0;
            ReadBits(ref bits, 2, end);
            ReadBits(ref bits, 8, end);
            if (full || ReadBits(ref bits, 1, end) != 0)
            {
                ReadBounded(ref bits, 6, 59, end);
                if (full || ReadBits(ref bits, 1, end) != 0)
                {
                    ReadBounded(ref bits, 6, 59, end);
                    if (full || ReadBits(ref bits, 1, end) != 0)
                    {
                        ReadBounded(ref bits, 5, 23, end);
                    }
                }
            }
            ReadBits(ref bits, offsetBits, end);
            return scan;
        }

        private static void ReadBounded(ref H264BitReader bits, int count, uint maximum, long end)
        {
            if (ReadBits(ref bits, count, end) > maximum)
            {
                throw new InvalidDataException("An H.264 clock timestamp field exceeds its permitted range.");
            }
        }

        private static uint ReadBits(ref H264BitReader bits, int count, long end)
        {
            if (bits.BitPosition + (long)count > end)
            {
                throw new InvalidDataException("The H.264 picture-timing payload is truncated.");
            }
            return bits.ReadBits(count);
        }

        private static H264DisplayStructure ReadStructure(uint code) => code switch
        {
            0 => H264DisplayStructure.Frame,
            1 => H264DisplayStructure.TopField,
            2 => H264DisplayStructure.BottomField,
            3 => H264DisplayStructure.TopBottom,
            4 => H264DisplayStructure.BottomTop,
            5 => H264DisplayStructure.TopBottomTop,
            6 => H264DisplayStructure.BottomTopBottom,
            7 => H264DisplayStructure.FrameDouble,
            8 => H264DisplayStructure.FrameTriple,
            _ => throw new InvalidDataException("The H.264 picture display structure is reserved.")
        };

        private static int ClockCount(H264DisplayStructure structure) => structure switch
        {
            H264DisplayStructure.Frame or H264DisplayStructure.TopField or H264DisplayStructure.BottomField => 1,
            H264DisplayStructure.TopBottom or H264DisplayStructure.BottomTop or H264DisplayStructure.FrameDouble => 2,
            H264DisplayStructure.TopBottomTop or H264DisplayStructure.BottomTopBottom or H264DisplayStructure.FrameTriple => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(structure))
        };
    }
}

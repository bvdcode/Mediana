// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core.Audio;

namespace Mediana.Codecs.Mp3
{
    internal class Mp3FrameInfo
    {
        private readonly Mp3GranuleInfo[] granules = new Mp3GranuleInfo[4];

        public int MainDataBegin { get; private set; }
        public int MainDataBits { get; private set; }
        public int ChannelCount { get; private set; }
        public int GranuleCount { get; private set; }
        public int LeftScaleFactorReuse { get; private set; }
        public int RightScaleFactorReuse { get; private set; }
        public ReadOnlySpan<Mp3GranuleInfo> Granules => granules.AsSpan(0, ChannelCount * GranuleCount);

        public Mp3FrameHeader Read(ReadOnlySpan<byte> frame)
        {
            ChannelCount = 0;
            GranuleCount = 0;
            MainDataBegin = 0;
            MainDataBits = 0;
            LeftScaleFactorReuse = 0;
            RightScaleFactorReuse = 0;
            if (frame.Length < Mp3FrameHeader.HeaderBytes)
            {
                throw new InvalidDataException("The MP3 frame header is truncated.");
            }
            Mp3FrameHeader header = Mp3FrameHeader.Read(frame[..Mp3FrameHeader.HeaderBytes]);
            if (frame.Length != header.FrameBytes)
            {
                throw new InvalidDataException("The MP3 packet must contain exactly one complete frame.");
            }
            int sideOffset = Mp3FrameHeader.HeaderBytes + (header.HasCrc ? sizeof(ushort) : 0);
            ReadOnlySpan<byte> side = frame.Slice(sideOffset, header.SideInfoBytes);
            if (header.HasCrc)
            {
                ushort expected = BinaryPrimitives.ReadUInt16BigEndian(frame[Mp3FrameHeader.HeaderBytes..]);
                ushort actual = Mp3Crc.Update(Mp3Crc.Update(ushort.MaxValue, frame.Slice(2, 2)), side);
                if (actual != expected)
                {
                    throw new InvalidDataException("The MP3 header and side information CRC is invalid.");
                }
            }
            bool mpeg1 = header.Version switch
            {
                MpegAudioVersion.Mpeg1 => true,
                MpegAudioVersion.Mpeg2 or MpegAudioVersion.Mpeg25 => false,
                _ => throw new InvalidOperationException()
            };
            Mp3BitReader bits = new(side, 0, side.Length * 8);
            int back = bits.Read(mpeg1 ? 9 : 8);
            bits.Skip((mpeg1, header.ChannelCount) switch
            {
                (true, 1) => 5,
                (true, 2) => 3,
                (false, 1) => 1,
                (false, 2) => 2,
                _ => throw new InvalidOperationException()
            });
            int leftReuse = mpeg1 ? bits.Read(4) : 0;
            int rightReuse = mpeg1 && header.ChannelCount == 2 ? bits.Read(4) : 0;
            int count = mpeg1 ? 2 : 1;
            int totalBits = 0;
            bool intensityStereo = header.ChannelMode == Mp3ChannelMode.JointStereo && (header.ModeExtension & 1) != 0;
            for (int granule = 0; granule < count; granule++)
            {
                for (int channel = 0; channel < header.ChannelCount; channel++)
                {
                    Mp3GranuleInfo info = ReadGranule(ref bits, mpeg1, intensityStereo && channel == 1);
                    if (info.BlockType == 2 && (channel == 0 ? leftReuse : rightReuse) != 0)
                    {
                        throw new InvalidDataException("MP3 short blocks cannot reuse scalefactors between granules.");
                    }
                    granules[granule * header.ChannelCount + channel] = info;
                    totalBits += info.PartBits;
                }
            }
            if (bits.Remaining != 0 || totalBits > (back + frame.Length - sideOffset - side.Length) * 8)
            {
                throw new InvalidDataException("The MP3 side information declares an invalid main-data extent.");
            }
            MainDataBegin = back;
            MainDataBits = totalBits;
            LeftScaleFactorReuse = leftReuse;
            RightScaleFactorReuse = rightReuse;
            ChannelCount = header.ChannelCount;
            GranuleCount = count;
            return header;
        }

        private static Mp3GranuleInfo ReadGranule(ref Mp3BitReader bits, bool mpeg1, bool intensityChannel)
        {
            int partBits = bits.Read(12);
            int bigValues = bits.Read(9);
            if (bigValues > 288)
            {
                throw new InvalidDataException("The MP3 spectral pair count exceeds a granule.");
            }
            int gain = bits.Read(8);
            int compression = bits.Read(mpeg1 ? 4 : 9);
            int block = 0;
            bool mixed = false;
            int table0;
            int table1;
            int table2 = 0;
            int subgain0 = 0;
            int subgain1 = 0;
            int subgain2 = 0;
            int region0;
            int region1;
            if (bits.Read(1) != 0)
            {
                block = bits.Read(2);
                if (block == 0)
                {
                    throw new InvalidDataException("The MP3 switched block type is reserved.");
                }
                mixed = bits.Read(1) != 0;
                table0 = ReadTable(ref bits);
                table1 = ReadTable(ref bits);
                subgain0 = bits.Read(3);
                subgain1 = bits.Read(3);
                subgain2 = bits.Read(3);
                region0 = block == 2 && !mixed ? 8 : 7;
                region1 = 63;
            }
            else
            {
                table0 = ReadTable(ref bits);
                table1 = ReadTable(ref bits);
                table2 = ReadTable(ref bits);
                region0 = bits.Read(4);
                region1 = bits.Read(3);
            }
            bool preflag = mpeg1 ? bits.Read(1) != 0 : !intensityChannel && compression >= 500;
            bool scale = bits.Read(1) != 0;
            int count1 = bits.Read(1);
            return new Mp3GranuleInfo(partBits, bigValues, gain, compression, block, mixed,
                table0, table1, table2, subgain0, subgain1, subgain2, region0, region1, preflag, scale, count1);
        }

        private static int ReadTable(ref Mp3BitReader bits)
        {
            int table = bits.Read(5);
            if (table is 4 or 14)
            {
                throw new InvalidDataException("The MP3 Huffman table is reserved.");
            }
            return table;
        }
    }
}

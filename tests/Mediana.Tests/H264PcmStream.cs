// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text;
using Mediana.Core;
using Mediana.Core.Timing;

namespace Mediana.Tests
{
    internal static class H264PcmStream
    {
        public static EncodedStreamInfo Create(int reorder = 2, int buffering = 3)
        {
            byte[] sequence = [0x67, .. H264TestBits.Rbsp("01001101 00000000 00001010 1 1 1 1 010 0 1 1 1 1 0 1 " +
                "00000000 1 1 " + H264TestBits.Ue(0) + H264TestBits.Ue(0) + H264TestBits.Ue(15) +
                H264TestBits.Ue(15) + H264TestBits.Ue(reorder) + H264TestBits.Ue(buffering))];
            byte[] picture = [0x68, .. H264TestBits.Rbsp("1 1 0 0 1 1 1 0 00 1 1 1 1 0 0")];
            byte[] configuration = [1, 77, 0, 10, 255, 225, (byte)(sequence.Length >> 8), (byte)sequence.Length, .. sequence,
                1, (byte)(picture.Length >> 8), (byte)picture.Length, .. picture];
            return new EncodedStreamInfo(0, MediaKind.Video, "h264", new TimeBase(1, 1), configuration);
        }

        public static byte[] Picture(int frameNumber, int order, byte sample, bool reset = false)
        {
            bool idr = frameNumber == 0;
            string marking = idr ? "00" : reset ? "1" + H264TestBits.Ue(5) + H264TestBits.Ue(0) : "0";
            StringBuilder bits = new("1 011 1".Replace(" ", string.Empty, StringComparison.Ordinal));
            bits.Append(Convert.ToString(frameNumber, 2).PadLeft(4, '0'));
            if (idr)
            {
                bits.Append(H264TestBits.Ue(0));
            }
            bits.Append(Convert.ToString(order, 2).PadLeft(4, '0'));
            bits.Append(marking);
            bits.Append('1');
            bits.Append(H264TestBits.Ue(1));
            bits.Append(H264TestBits.Ue(25));
            while (bits.Length % 8 != 0)
            {
                bits.Append('0');
            }
            for (int index = 0; index < 384; index++)
            {
                bits.Append(Convert.ToString(sample, 2).PadLeft(8, '0'));
            }
            byte[] nal = [idr ? (byte)0x65 : (byte)0x61, .. H264TestBits.Rbsp(bits.ToString())];
            return H264AccessUnitTests.Packet(nal);
        }
    }
}

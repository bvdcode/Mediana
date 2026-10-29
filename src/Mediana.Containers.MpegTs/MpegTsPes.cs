// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Containers.MpegTs
{
    internal static class MpegTsPes
    {
        public static int Write(Span<byte> output, int streamId, int payloadLength, long pts, long dts, bool video)
        {
            bool distinct = pts != dts;
            int timestamps = distinct ? 10 : 5;
            BinaryPrimitives.WriteUInt32BigEndian(output, (uint)(0x100 | streamId));
            BinaryPrimitives.WriteUInt16BigEndian(output[4..], video ? (ushort)0 : checked((ushort)(payloadLength + 3 + timestamps)));
            output[6] = 0x84;
            output[7] = distinct ? (byte)0xC0 : (byte)0x80;
            output[8] = (byte)timestamps;
            Timestamp(output[9..], pts, distinct ? 3 : 2);
            if (distinct)
            {
                Timestamp(output[14..], dts, 1);
            }
            return 9 + timestamps;
        }

        private static void Timestamp(Span<byte> output, long value, int prefix)
        {
            ulong bits = (ulong)value & 0x1FFFFFFFF;
            output[0] = (byte)((ulong)prefix << 4 | ((bits >> 30) & 7) << 1 | 1);
            output[1] = (byte)(bits >> 22);
            output[2] = (byte)(((bits >> 15) & 0x7F) << 1 | 1);
            output[3] = (byte)(bits >> 7);
            output[4] = (byte)((bits & 0x7F) << 1 | 1);
        }
    }
}

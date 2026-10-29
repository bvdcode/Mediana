// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.MpegTs
{
    internal static class MpegTsPacket
    {
        public const int Size = 188;
        public const int PayloadSize = 184;

        public static int Payload(Span<byte> packet, int pid, int counter, bool start, ReadOnlySpan<byte> payload,
            long? pcr = null, bool randomAccess = false)
        {
            int minimumAdaptation = pcr.HasValue ? 8 : randomAccess ? 2 : 0;
            int count = Math.Min(payload.Length, PayloadSize - minimumAdaptation);
            int adaptation = PayloadSize - count;
            Header(packet, pid, counter, start, adaptation == 0 ? 1 : 3);
            int offset = 4;
            if (adaptation != 0)
            {
                packet[offset++] = (byte)(adaptation - 1);
                if (adaptation > 1)
                {
                    packet[offset++] = (byte)((pcr.HasValue ? 0x10 : 0) | (randomAccess ? 0x40 : 0));
                    if (pcr.HasValue)
                    {
                        ClockReference(packet[offset..], pcr.Value);
                        offset += 6;
                    }
                }
                offset = 4 + adaptation;
            }
            payload[..count].CopyTo(packet[offset..]);
            return count;
        }

        public static void Clock(Span<byte> packet, int pid, int counter, long pcr)
        {
            Header(packet, pid, counter, false, 2);
            packet[4] = 183;
            packet[5] = 0x10;
            ClockReference(packet[6..], pcr);
        }

        public static void Header(Span<byte> packet, int pid, int counter, bool start, int adaptationControl)
        {
            packet.Fill(0xFF);
            packet[0] = 0x47;
            packet[1] = (byte)((start ? 0x40 : 0) | pid >> 8);
            packet[2] = (byte)pid;
            packet[3] = (byte)(adaptationControl << 4 | counter);
        }

        private static void ClockReference(Span<byte> output, long value)
        {
            ulong bits = (ulong)value & 0x1FFFFFFFF;
            output[0] = (byte)(bits >> 25);
            output[1] = (byte)(bits >> 17);
            output[2] = (byte)(bits >> 9);
            output[3] = (byte)(bits >> 1);
            output[4] = (byte)((bits & 1) << 7 | 0x7E);
            output[5] = 0;
        }
    }
}

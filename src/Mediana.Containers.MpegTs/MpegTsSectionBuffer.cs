// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.MpegTs
{
    internal class MpegTsSectionBuffer
    {
        private readonly byte[] buffer = new byte[1024];
        private int written;
        private int length;

        public void Accept(ReadOnlySpan<byte> payload, bool startsSection, Action<ReadOnlyMemory<byte>> receive)
        {
            if (!startsSection)
            {
                if (written != 0) { Consume(payload, receive, false); }
                return;
            }
            if (payload.IsEmpty || payload[0] >= payload.Length)
            {
                throw new InvalidDataException("The MPEG-TS PSI pointer exceeds its packet.");
            }
            int pointer = payload[0];
            if (written != 0)
            {
                Consume(payload.Slice(1, pointer), receive, false);
                if (written != 0)
                {
                    throw new InvalidDataException("The MPEG-TS PSI section ended before its declared size.");
                }
            }
            Consume(payload[(1 + pointer)..], receive, true);
        }

        private void Consume(ReadOnlySpan<byte> payload, Action<ReadOnlyMemory<byte>> receive, bool allowNext)
        {
            while (!payload.IsEmpty)
            {
                if (written == 0 && (!allowNext || payload[0] == 0xFF)) { return; }
                int target = length == 0 ? 3 : length;
                int count = Math.Min(target - written, payload.Length);
                payload[..count].CopyTo(buffer.AsSpan(written));
                written += count;
                payload = payload[count..];
                if (written == 3 && length == 0)
                {
                    length = 3 + ((buffer[1] & 15) << 8 | buffer[2]);
                    if (length is < 8 or > 1024)
                    {
                        throw new InvalidDataException("The MPEG-TS PSI section length is invalid.");
                    }
                }
                if (length != 0 && written == length)
                {
                    if (MpegTsTables.Crc(buffer.AsSpan(0, length)) != 0)
                    {
                        throw new InvalidDataException("The MPEG-TS PSI checksum is invalid.");
                    }
                    receive(buffer.AsMemory(0, length));
                    written = length = 0;
                }
            }
        }
    }
}

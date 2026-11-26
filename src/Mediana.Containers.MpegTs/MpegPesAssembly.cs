// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.MpegTs
{
    internal class MpegPesAssembly(long start, bool randomAccess)
    {
        private readonly List<MpegPayloadRange> ranges = [];
        private readonly byte[] header = new byte[MpegPesHeader.MaximumHeaderLength];
        private int headerBytes;

        public void Add(long offset, ReadOnlySpan<byte> payload)
        {
            ranges.Add(new(offset, payload.Length));
            int copied = Math.Min(header.Length - headerBytes, payload.Length);
            payload[..copied].CopyTo(header.AsSpan(headerBytes));
            headerBytes += copied;
        }

        public MpegSystemsPacket? Finish(MpegSystemsTrack track) =>
            track.Add(start, ranges, MpegPesHeader.Read(header.AsSpan(0, headerBytes)), randomAccess ? true : null);
    }
}

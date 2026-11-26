// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Containers.MpegTs
{
    internal class MpegSystemsPacket(int streamIndex, long position, MpegPayloadRange[] ranges, int length,
        MediaTimestamp? presentationTime, MediaTimestamp? decodeTime, bool? randomAccess)
    {
        public int StreamIndex { get; } = streamIndex;
        public long Position { get; } = position;
        public MpegPayloadRange[] Ranges { get; } = ranges;
        public int Length { get; } = length;
        public MediaTimestamp? PresentationTime { get; } = presentationTime;
        public MediaTimestamp? DecodeTime { get; } = decodeTime;
        public bool? RandomAccess { get; } = randomAccess;
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;

namespace Mediana.Codecs.H264
{
    internal partial class H264Picture
    {
        internal bool HasPackedResiduals => packedResiduals;

        internal Memory<byte> PackedResidualStorage => packedResiduals ? metadata[..modesOffset] : default;

        internal void StoreCount(int plane, int position, int count)
        {
            if (!packedResiduals)
            {
                Counts(plane)[position] = (byte)count;
            }
        }

        internal void StoreCount(int plane, int x, int y, int count)
        {
            if (!packedResiduals)
            {
                Counts(plane)[BlockIndex(plane, x, y)] = (byte)count;
            }
        }

        internal void MarkPcmResiduals(int address)
        {
            if (packedResiduals)
            {
                MemoryMarshal.Cast<byte, uint>(PackedResidualStorage.Span)[address] = H264CodedBlockFlags.All;
            }
        }
    }
}

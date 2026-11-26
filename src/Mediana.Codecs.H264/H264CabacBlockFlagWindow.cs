// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;

namespace Mediana.Codecs.H264
{
    internal class H264CabacBlockFlagWindow(Memory<byte> storage)
    {
        private int address;
        private uint current;
        private uint leftFlags;
        private uint topFlags;

        private Span<uint> Flags => MemoryMarshal.Cast<byte, uint>(storage.Span);

        public void Begin(int macroblock, int left, int top, bool intra)
        {
            address = macroblock;
            current = 0;
            ReadOnlySpan<uint> flags = Flags;
            uint unavailable = intra ? H264CodedBlockFlags.All : 0;
            leftFlags = H264CodedBlockFlags.LeftBorder(left < 0 ? unavailable : flags[left]);
            topFlags = H264CodedBlockFlags.TopBorder(top < 0 ? unavailable : flags[top]);
        }

        public int Context(int block) => (int)(((leftFlags >> block) & 1u) | (((topFlags >> block) & 1u) << 1));

        public void Mark4x4(int block) => Mark(1u << block);

        public void Mark8x8(int block) => Mark(0x33u << block);

        public void Complete() => Flags[address] = current;

        private void Mark(uint flags)
        {
            current |= flags;
            leftFlags |= H264CodedBlockFlags.InteriorLeft(flags);
            topFlags |= H264CodedBlockFlags.InteriorTop(flags);
        }
    }
}

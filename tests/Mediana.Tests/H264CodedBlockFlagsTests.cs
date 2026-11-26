// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Codecs.H264;
using Xunit;

namespace Mediana.Tests
{
    public class H264CodedBlockFlagsTests
    {
        [Fact]
        public void BorderAndInteriorMasksKeepAllThreePlanesIndependent()
        {
            for (int plane = 0; plane < 3; plane++)
            {
                int size = plane == 0 ? 4 : 2;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        uint flag = At(plane, x, y);
                        Assert.Equal(x == size - 1 ? At(plane, 0, y) : 0u, H264CodedBlockFlags.LeftBorder(flag));
                        Assert.Equal(y == size - 1 ? At(plane, x, 0) : 0u, H264CodedBlockFlags.TopBorder(flag));
                        Assert.Equal(x + 1 < size ? At(plane, x + 1, y) : 0u, H264CodedBlockFlags.InteriorLeft(flag));
                        Assert.Equal(y + 1 < size ? At(plane, x, y + 1) : 0u, H264CodedBlockFlags.InteriorTop(flag));
                    }
                }
            }
        }

        [Fact]
        public void MacroblockWindowUsesCurrentResidualsAndPublishesThemOnce()
        {
            byte[] storage = new byte[3 * sizeof(uint)];
            Span<uint> published = MemoryMarshal.Cast<byte, uint>(storage.AsSpan());
            H264CabacBlockFlagWindow window = new(storage);
            window.Begin(0, -1, -1, intra: true);
            Assert.Equal(3, window.Context(0));
            Assert.Equal(2, window.Context(1));
            Assert.Equal(1, window.Context(4));
            Assert.Equal(0, window.Context(5));

            window.Mark8x8(2);
            Assert.Equal(3, window.Context(3));
            Assert.Equal(2, window.Context(6));
            Assert.Equal(2, window.Context(10));
            Assert.Equal(0u, published[0]);
            window.Complete();
            Assert.Equal(0xCCu, published[0]);

            published[1] = H264CodedBlockFlags.All;
            window.Begin(2, 0, 1, intra: false);
            Assert.Equal(3, window.Context(0));
            Assert.Equal(1, window.Context(4));
            Assert.Equal(2, window.Context(16));
            window.Mark4x4(16);
            Assert.Equal(3, window.Context(17));
            Assert.Equal(2, window.Context(18));
            window.Complete();
            Assert.Equal(1u << 16, published[2]);
        }

        private static uint At(int plane, int x, int y) =>
            1u << (plane == 0 ? 4 * y + x : 12 + 4 * plane + 2 * y + x);
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Webp;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class WebpTokenBufferTests
    {
        [Fact]
        public void CompactTokensPreserveAllLiteralBitsAndReferencesAcrossBlockBoundaries()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            List<ulong> expected = [];
            using (WebpTokenBuffer buffer = new(allocator))
            {
                for (int index = 0; index < 16000; index++)
                {
                    ulong value = index % 4095 == 4094 || index % 7 == 6 ? (4096ul << 32) | 1048575u : (uint)(index * 73871) | 0xff000000u;
                    expected.Add(value);
                    buffer.Add(value);
                }
                List<ulong> actual = [];
                for (int block = 0; block < buffer.BlockCount; block++)
                {
                    ReadOnlySpan<uint> values = buffer.GetBlock(block);
                    ReadOnlySpan<uint> references = buffer.GetReferences(block);
                    for (int index = 0; index < values.Length; index++)
                    {
                        ulong value = values[index];
                        if ((references[index / 32] & (1u << (index & 31))) != 0) { value |= (ulong)values[++index] << 32; }
                        actual.Add(value);
                    }
                }
                Assert.Equal(expected, actual);
                Assert.True(allocator.AllocatedBytes < expected.Count * sizeof(ulong));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}

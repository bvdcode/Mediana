// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Webp;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class Vp8BooleanWriterTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(128)]
        [InlineData(231)]
        [InlineData(254)]
        [InlineData(255)]
        [InlineData(-1)]
        public void ExtremeProbabilitiesCarriesAndBlockBoundariesRoundTrip(int probability)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            byte[] encoded;
            Random random = new(71361);
            int[] probabilities = new int[40000];
            bool[] values = new bool[probabilities.Length];
            using (WebpByteBuffer storage = new(allocator))
            {
                Vp8BooleanWriter writer = new(storage);
                for (int index = 0; index < values.Length; index++)
                {
                    values[index] = random.Next(2) != 0;
                    probabilities[index] = probability < 0 ? random.Next(256) : probability;
                    writer.Write(values[index], probabilities[index]);
                }
                writer.Literal(0x7135a, 20);
                writer.Finish();
                encoded = new byte[storage.Length];
                storage.CopyTo(encoded);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            Vp8TestBooleanReader reader = new(encoded);
            for (int index = 0; index < values.Length; index++) { Assert.Equal(values[index], reader.Read(probabilities[index])); }
            Assert.Equal(0x7135a, reader.Literal(20));
        }
    }
}

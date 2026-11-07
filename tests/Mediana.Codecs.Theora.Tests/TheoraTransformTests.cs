// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Codecs.Theora.Tests
{
    public class TheoraTransformTests
    {
        [Fact]
        public async Task IntegerTransformsMatchNativeReferenceAcrossBasisAndOverflowInputs()
        {
            byte[] data = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "idct.bin"));
            int offset = 0;
            foreach (int size in new[] { 8, 64 })
            {
                int count = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset)); offset += 4;
                short[] actual = new short[size];
                for (int c = 0; c < count; c++)
                {
                    for (int i = 0; i < size; i++) { actual[i] = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(offset)); offset += 2; }
                    if (size == 8) { TheoraInverseDct.Transform1D(actual); } else { TheoraInverseDct.Transform(actual); }
                    for (int i = 0; i < size; i++)
                    {
                        short expected = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(offset)); offset += 2;
                        Assert.Equal(expected, actual[i]);
                    }
                }
            }
            Assert.Equal(data.Length, offset);
        }
    }
}

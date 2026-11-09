// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1LargeTransformTests
    {
        [Theory]
        [InlineData("quantizedTx8")]
        [InlineData("quantizedTx16")]
        [InlineData("quantizedTx32")]
        [InlineData("quantizedTx64")]
        public void NativePicturesActuallyConsumeLargerTransformContexts(string fixture)
        {
            byte[] encoded = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", fixture + ".obu"));
            Av1ObuReader obus = new(encoded);
            Av1SequenceHeader sequence = default;
            ReadOnlySpan<byte> frame = default;
            while (obus.TryRead(out int type, out ReadOnlySpan<byte> payload))
            {
                if (type == 1) { sequence = Av1SequenceHeader.Parse(payload); }
                if (type == 6) { frame = payload; }
            }
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (Av1ReferencePictures references = new())
            {
                Av1FrameHeader header = Av1FrameHeader.Parse(frame, sequence, references);
                Span<ushort> probabilities = stackalloc ushort[Av1DefaultCdfs.Count];
                using WritableMediaBuffer pixels = allocator.Allocate(sequence.SampleCount);
                using WritableMediaBuffer decoded = allocator.Allocate(sequence.SampleCount / 16);
                using WritableMediaBuffer contexts = allocator.Allocate(sequence.ContextCount * Unsafe.SizeOf<Av1BlockContext<byte>>());
                Av1TileDecoder<byte> decoder = new(frame[header.TileOffset..], sequence, header, pixels.Span, probabilities,
                    decoded.Span, MemoryMarshal.Cast<byte, Av1BlockContext<byte>>(contexts.Span), default);
                decoder.Decode();
                int reads = 0;
                for (int category = 1; category < 5; category++)
                {
                    for (int context = 0; context < 13; context++)
                    {
                        reads += probabilities[Av1DefaultCdfs.AllZeroOffset + category * 39 + context * 3 + 2];
                    }
                }
                Assert.True(reads > 0);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}

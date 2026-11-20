// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class H264AdaptivePredictionTestInput
    {
        public static void Verify()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            foreach (int depth in new[] { 8, 10 })
            {
                foreach (int chroma in new[] { 1, 2 })
                {
                    VerifyPrediction(allocator, depth, chroma);
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("Adaptive prediction retained picture storage.");
            }
        }

        private static void VerifyPrediction(MediaBufferAllocator allocator, int depth, int chroma)
        {
            H264SliceHeader header = H264BidirectionalPredictionTestInput.Header();
            H264SequenceParameterSet sequence = header.Sequence with
            {
                WidthInMbs = 2, HeightInMapUnits = 2, Width = 32, Height = 64,
                FrameMbsOnly = false, MbAdaptiveFrameField = true,
                LumaBitDepth = depth, ChromaBitDepth = depth, ChromaFormat = chroma
            };
            header = header with
            {
                Sequence = sequence, Kind = H264SliceKind.P, ActiveReferencesL0 = 2,
                Weights = new H264WeightTable(0, 0, [new(1, 3, 1, 5, 1, 7), new(2, 3, 2, 5, 2, 7)])
            };
            using H264Picture first = new(sequence, allocator, H264SliceKind.P);
            using H264Picture second = new(sequence, allocator, H264SliceKind.P);
            using H264Picture target = new(sequence, allocator, H264SliceKind.P);
            Fill(first, 20);
            Fill(second, 60);
            H264BidirectionalReferences frames = new([new(first, 0, new(0, 1)), new(second, 1, new(2, 3))], []);
            long allocated = allocator.AllocatedBytes;
            H264AdaptiveReferences adaptive = new(frames);
            if (allocator.AllocatedBytes != allocated || !ReferenceEquals(frames, adaptive.Get(H264PictureStructure.Frame)))
            {
                throw new InvalidOperationException("Reference expansion copied pictures or replaced the frame list.");
            }
            target.CaptureReferences(0, frames.L0, []);
            for (int address = 0; address < target.Layout.MacroblockCount; address++)
            {
                bool field = address / 2 % 2 == 0;
                if ((address & 1) == 0)
                {
                    target.Grid.SetFieldPair(address, field);
                }
                (int x, int y) = target.Grid.Position(address);
                H264MacroblockPredictionContext context = H264MacroblockPredictionContext.Create(target, x + 4, y + 8);
                int expectedY = field ? address / 4 * 16 + 8 : y + 8;
                H264PictureStructure structure = (field, address & 1) switch
                {
                    (false, _) => H264PictureStructure.Frame,
                    (true, 0) => H264PictureStructure.TopField,
                    (true, 1) => H264PictureStructure.BottomField,
                    _ => throw new InvalidOperationException("Invalid pair parity.")
                };
                int count = field ? 4 : 2;
                if (context.X != x + 4 || context.Y != expectedY || context.Structure != structure ||
                    context.ActiveReferenceCount(2) != count || context.AdaptiveField != field)
                {
                    throw new InvalidDataException("Adaptive prediction selected incorrect sample coordinates or reference bounds.");
                }
                H264BidirectionalReferences selected = adaptive.Get(structure);
                if (!ReferenceEquals(selected, adaptive.Get(structure)) || selected.L0.Count != count)
                {
                    throw new InvalidOperationException("Adaptive reference lists must be reused for every macroblock.");
                }
                for (int index = 0; index < count; index++)
                {
                    H264ReferencePicture reference = selected.L0[index]!;
                    if (target.ReferenceIdentity(H264ReferenceList.L0, index, address) != reference.Identity ||
                        context.WeightIndex(index) != (field ? index / 2 : index))
                    {
                        throw new InvalidDataException("Adaptive reference identity or weight index differs from the selected field.");
                    }
                    VerifyWeightedBlock(allocator, target, header, selected.L0, address, index, field);
                }
            }
        }

        private static void VerifyWeightedBlock(MediaBufferAllocator allocator, H264Picture target, H264SliceHeader header,
            IReadOnlyList<H264ReferencePicture?> references, int address, int index, bool field)
        {
            using H264BitWriter writer = new(allocator, 128, 0x41);
            if (field)
            {
                writer.WriteUnsigned((uint)index);
            }
            else
            {
                writer.WriteFlag(index == 0);
            }
            writer.WriteSigned(0);
            writer.WriteSigned(0);
            using MediaBuffer nal = writer.Finish();
            H264BitReader bits = H264Nal.Payload(nal.Memory.Span, 1);
            (int x, int y) = target.Grid.Position(address);
            byte[] prediction = new byte[target.LumaMacroblockBytes + 2 * target.ChromaMacroblockBytes];
            H264InterPrediction.Read(ref bits, new H264CavlcPredictionReader(), target, header, references, x, y, 0, prediction);
            bits.ReadTrailingBits();
            int frameIndex = field ? index / 2 : index;
            int position = 0;
            for (int plane = 0; plane < 3; plane++)
            {
                int columns = plane == 0 ? 16 : 8;
                int rows = 16 / target.VerticalScale(plane);
                for (int row = 0; row < rows; row++)
                {
                    int parity = field ? (address + index) % 2 : row % 2;
                    int expected = ((20 + 40 * frameIndex + 20 * parity) * (frameIndex + 1) + 3 + plane * 2)
                        << (target.Sequence.LumaBitDepth - 8);
                    ReadOnlySpan<byte> line = prediction.AsSpan(position, columns * target.SampleBytes);
                    bool matches = target.SampleBytes switch
                    {
                        1 => line.IndexOfAnyExcept((byte)expected) < 0,
                        2 => MemoryMarshal.Cast<byte, ushort>(line).IndexOfAnyExcept((ushort)expected) < 0,
                        _ => throw new InvalidDataException("Invalid sample size.")
                    };
                    if (!matches)
                    {
                        throw new InvalidDataException($"Adaptive weighted prediction differs at address {address}, reference {index}, plane {plane}, row {row}.");
                    }
                    position += line.Length;
                }
            }
        }

        internal static void Fill(H264Picture picture, int start)
        {
            for (int plane = 0; plane < 3; plane++)
            {
                int stride = picture.Stride(plane) * picture.SampleBytes;
                for (int row = 0; row < picture.Height / picture.VerticalScale(plane); row++)
                {
                    int value = (start + 20 * (row % 2)) << (picture.Sequence.LumaBitDepth - 8);
                    Span<byte> line = picture.Plane(plane).Slice(row * stride, stride);
                    if (picture.SampleBytes == 1)
                    {
                        line.Fill((byte)value);
                    }
                    else
                    {
                        MemoryMarshal.Cast<byte, ushort>(line).Fill((ushort)value);
                    }
                }
            }
        }
    }
}

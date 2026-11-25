// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class H264CabacBidirectionalTests
    {
        [Fact]
        public Task ContextsAndEveryMacroblockAndSubtypeMatchNormativeStatesAndExactIntervals() =>
            H264CabacBidirectionalTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "H264"), Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"));

        [Fact]
        public async Task SpatialSkipRunPreservesWeightedPredictionAroundCodedBlock()
        {
            string data = Path.Combine(AppContext.BaseDirectory, "Data");
            H264SliceHeader header = await H264CabacPredictionTestInput.ReadHeaderAsync(Path.Combine(data, "Mp4"));
            header = header with
            {
                Kind = H264SliceKind.B,
                Sequence = header.Sequence with { WidthInMbs = 4, HeightInMapUnits = 2, Width = 64, Height = 32 },
                Picture = header.Picture with { WeightedBiPrediction = 2 },
                DirectSpatialMotionPrediction = true,
                ActiveReferencesL0 = 1,
                ActiveReferencesL1 = 1,
                Weights = null,
                DeblockingIdc = 1
            };
            byte[] bidirectional = await File.ReadAllBytesAsync(Path.Combine(data, "H264", "cabac-bidirectional-contexts.bin"));
            byte[] shared = await File.ReadAllBytesAsync(Path.Combine(data, "H264", "cabac-predictive-contexts.bin"));
            H264CabacIntervalWriter writer = H264CabacBidirectionalTestInput.Writer(bidirectional, shared, header);
            for (int address = 0; address < 8; address++)
            {
                writer.Decision(address != 2, address is 3 or 6 ? 25 : 24, 0, 0);
                if (address == 2)
                {
                    foreach (int context in new[] { 27, 74, 74, 76, 76, 77 })
                    {
                        writer.Decision(false, context, 0, 0);
                    }
                }
                writer.Termination(address == 7);
            }

            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264Picture past = new(header.Sequence, allocator, H264SliceKind.P);
            using H264Picture future = new(header.Sequence, allocator, H264SliceKind.P);
            for (int plane = 0; plane < 3; plane++)
            {
                past.Plane(plane).Fill((byte)(32 + plane));
                future.Plane(plane).Fill((byte)(96 + plane));
            }
            H264BidirectionalReferences references = new([new(past, 0, new(0, 0))], [new(future, 1, new(4, 4))]);
            H264BitReader bits = new(writer.ToEbsp(endAtArithmeticByte: true));
            using H264Picture decoded = H264CabacPictureDecoder.Decode(ref bits, header, references, new(1, 1), allocator);
            for (int plane = 0; plane < 3; plane++)
            {
                Assert.False(decoded.ReadPlane(plane).ContainsAnyExcept((byte)(48 + plane)));
            }
            for (int address = 0; address < 8; address++)
            {
                Assert.Equal(address != 2, decoded.MacroblockFlags[address].HasFlag(H264MacroblockFlags.StationarySkip));
            }
        }
    }
}

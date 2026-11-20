// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class H264MbaffSequenceTestInput
    {
        public static async Task<(int Frames, int Fields)> VerifyPredictiveAsync(string root, string name, int count, bool cabac = false, int[]? displayOrder = null, bool deblocking = false)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            H264PictureOrderState order = new();
            int framePairs = 0;
            int fieldPairs = 0;
            using (H264ReferenceBuffer references = new())
            {
                for (int index = 0; index < count; index++)
                {
                    (H264Configuration configuration, byte[] nal) = await H264TestInput.ReadSliceAsync(Path.Combine(root, name + ".mp4"), index);
                    H264SequenceParameterSet sequence = configuration.Sequences.Values.Single();
                    int displayIndex = displayOrder is null ? index : displayOrder[index];
                    byte[] expected = await H264FieldTestInput.ReadReferenceAsync(root, name, displayIndex, sequence);
                    (order, int fields) = DecodeAndStore(configuration, nal, expected, order, references, allocator, index, cabac, displayOrder is not null, deblocking);
                    fieldPairs += fields;
                    framePairs += sequence.WidthInMbs * sequence.HeightInMapUnits - fields;
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("MBAFF predictive reconstruction retained picture storage.");
            }
            return (framePairs, fieldPairs);
        }

        private static (H264PictureOrderState Order, int Fields) DecodeAndStore(H264Configuration configuration,
            ReadOnlySpan<byte> nal, ReadOnlySpan<byte> expected, H264PictureOrderState order,
            H264ReferenceBuffer references, MediaBufferAllocator allocator, int index, bool cabac, bool bidirectional, bool deblocking)
        {
            H264BitReader bits = H264Nal.Payload(nal, H264Nal.Type(nal));
            H264SliceHeader header = H264SliceHeaderReader.Read(ref bits, configuration, nal[0]);
            H264SliceKind kind = (bidirectional, index) switch
            {
                (_, 0) => H264SliceKind.I,
                (true, > 0) when index % 2 == 0 => H264SliceKind.B,
                (_, > 0) => H264SliceKind.P,
                _ => throw new ArgumentOutOfRangeException(nameof(index))
            };
            if (!header.Layout.MacroblockAdaptiveFrameField || header.Kind != kind ||
                header.Picture.Cabac != cabac || header.DeblockingEnabled != deblocking)
            {
                throw new InvalidDataException("Expected an independent MBAFF sequence with the requested entropy coding and deblocking.");
            }
            H264PictureOrderState next = order.Advance(header);
            H264BidirectionalReferences lists = kind switch
            {
                H264SliceKind.I => new([], []),
                H264SliceKind.P => new(references.GetList0(header), []),
                H264SliceKind.B => references.GetBidirectionalLists(header, next.Current),
                _ => throw new InvalidDataException("Unexpected slice kind.")
            };
            H264Picture picture = new(header.Sequence, allocator, header.Kind, header.Structure);
            bool stored = false;
            try
            {
                picture.CaptureReferences(0, lists.L0, lists.L1);
                if (cabac)
                {
                    H264CabacPictureDecoder.DecodeSlice(ref bits, header, lists, next.Current, picture,
                        header.Layout.MacroblockCount, allocator);
                }
                else if (kind == H264SliceKind.B)
                {
                    H264BidirectionalPictureDecoder.DecodeSlice(ref bits, header, lists, next.Current, picture, header.Layout.MacroblockCount);
                }
                else
                {
                    H264PictureDecoder.DecodeSlice(ref bits, header, lists.L0, picture, header.Layout.MacroblockCount);
                }
                H264Deblocking.Apply(picture, header, CancellationToken.None, lists.L0, lists.L1);
                H264FieldTestInput.VerifyPicture(picture, expected, index);
                int fields = 0;
                for (int address = 0; address < header.Layout.MacroblockCount; address += 2)
                {
                    if (picture.Grid.IsField(address))
                    {
                        fields++;
                    }
                }
                references.Store(picture, header, next.Current);
                stored = true;
                return (next, fields);
            }
            finally
            {
                if (!stored)
                {
                    picture.Dispose();
                }
            }
        }
    }
}

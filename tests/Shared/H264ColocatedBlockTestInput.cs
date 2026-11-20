// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class H264ColocatedBlockTestInput
    {
        public static void Verify()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            foreach ((int current, int source) in new[] { (0, 0), (0, 1), (1, 0), (1, 1), (1, 2), (2, 1), (2, 2) })
            {
                foreach (bool bottom in new[] { false, true })
                {
                    foreach (bool currentField in new[] { false, true })
                    {
                        foreach (bool sourceField in new[] { false, true })
                        {
                            foreach (int order in new[] { 4, 6, 8 })
                            {
                                VerifyMapping(allocator, current, source, bottom, currentField, sourceField, order);
                            }
                        }
                    }
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("Co-located block mapping retained picture buffers.");
            }
        }

        private static void VerifyMapping(MediaBufferAllocator allocator, int currentKind, int sourceKind,
            bool bottom, bool currentField, bool sourceField, int orderValue)
        {
            H264SequenceParameterSet basis = new() { Width = 48, Height = 128, WidthInMbs = 3, HeightInMapUnits = 4 };
            H264PictureStructure parity = bottom ? H264PictureStructure.BottomField : H264PictureStructure.TopField;
            using H264Picture target = new(basis with { MbAdaptiveFrameField = currentKind == 2 }, allocator,
                H264SliceKind.B, currentKind == 1 ? parity : H264PictureStructure.Frame);
            using H264Picture source = new(basis with { MbAdaptiveFrameField = sourceKind == 2 }, allocator,
                H264SliceKind.P, sourceKind == 1 ? H264PictureStructure.TopField : H264PictureStructure.Frame);
            using H264Picture lower = new(basis, allocator, H264SliceKind.P, H264PictureStructure.BottomField);
            SetPairs(target, currentField);
            SetPairs(source, sourceField);
            H264ReferencePicture reference;
            if (sourceKind == 1)
            {
                H264ReferencePicture top = new(source, 0, new(4, 0, H264PictureStructure.TopField));
                H264ReferencePicture low = new(lower, 0, new(0, 8, H264PictureStructure.BottomField))
                {
                    FrameIdentity = top.FrameIdentity
                };
                reference = H264ReferencePicture.GetFrame(top, low);
                if (currentKind == 1)
                {
                    reference = reference.GetField(parity);
                }
            }
            else
            {
                reference = new(source, 0, new(4, 8));
            }
            H264PictureOrder order = new(orderValue, orderValue + 1, target.Structure);
            long allocated = allocator.AllocatedBytes;
            for (int address = 0; address < target.Layout.MacroblockCount; address++)
            {
                (int x, int y) = target.Grid.Position(address);
                H264MacroblockPredictionContext context = H264MacroblockPredictionContext.Create(target, x, y);
                H264ReferencePicture selected = context.AdaptiveField && sourceKind == 1
                    ? reference.GetField(context.Structure) : reference;
                H264ColocatedPicture colocated = new(selected, target, context, order);
                bool selectBottom = currentKind == 1 ? bottom : currentKind == 2 && currentField
                    ? (address & 1) != 0 : Math.Abs(8 - order.Value) <= Math.Abs(4 - order.Value);
                H264Picture expectedPicture = sourceKind == 1 && selectBottom ? lower : source;
                for (int localY = 0; localY < 16; localY += 4)
                {
                    for (int localX = 0; localX < 16; localX += 4)
                    {
                        (int expectedAddress, int mappedY) = (currentKind, sourceKind) switch
                        {
                            (0, 0) or (1, 1) => (address, localY),
                            (1, 0) => (6 * (address / 3) + address % 3 + 3 * (localY / 8), localY * 2 % 16),
                            (1, 2) when sourceField => (address * 2 + (bottom ? 1 : 0), localY),
                            (1, 2) => (address * 2 + localY / 8, localY * 2 % 16),
                            (0, 1) => (3 * (address / 6) + address % 3, 8 * (address / 3 % 2) + 4 * (localY / 8)),
                            (2, 1) => (address / 2, currentField ? localY : 8 * (address & 1) + 4 * (localY / 8)),
                            (2, 2) when currentField == sourceField => (address, localY),
                            (2, 2) when currentField => ((address & ~1) + localY / 8, localY * 2 % 16),
                            (2, 2) => ((address & ~1) + (selectBottom ? 1 : 0), 8 * (address & 1) + 4 * (localY / 8)),
                            _ => throw new InvalidOperationException("Invalid co-located mapping case.")
                        };
                        (int expectedX, int expectedY) = expectedPicture.Grid.Position(expectedAddress);
                        H264ColocatedBlock block = colocated.MapBlock(x + localX, y + localY);
                        bool expectedField = sourceKind == 1 || sourceKind == 2 && sourceField;
                        if (block.Picture != expectedPicture || block.Address != expectedAddress ||
                            block.X != expectedX + localX || block.Y != expectedY + mappedY || block.Field != expectedField)
                        {
                            throw new InvalidDataException($"Co-located block differs for structures {currentKind}/{sourceKind}, address {address}, location ({localX},{localY}).");
                        }
                        VerifyMotion(block, currentKind == 1 || currentKind == 2 && currentField, expectedField, context.Structure);
                    }
                }
            }
            if (allocator.AllocatedBytes != allocated)
            {
                throw new InvalidOperationException("Co-located block mapping allocated pixel storage.");
            }
        }

        private static void SetPairs(H264Picture picture, bool field)
        {
            if (picture.Layout.MacroblockAdaptiveFrameField)
            {
                for (int address = 0; address < picture.Layout.MacroblockCount; address += 2)
                {
                    picture.Grid.SetFieldPair(address, field);
                }
            }
        }

        private static void VerifyMotion(H264ColocatedBlock block, bool currentField, bool sourceField, H264PictureStructure structure)
        {
            H264PictureIdentity frame = block.Picture.Identity;
            H264PictureIdentity identity = sourceField ? frame.ForStructure(H264PictureStructure.BottomField) : frame;
            H264PictureIdentity expectedIdentity = (currentField, sourceField) switch
            {
                (true, false) => frame.ForStructure(structure),
                (false, true) => frame,
                (true, true) or (false, false) => identity
            };
            foreach (int value in new[] { -32768, -11, -1, 0, 1, 11, 32767 })
            {
                int expected = (currentField, sourceField) switch
                {
                    (true, false) => value / 2,
                    (false, true) => value * 2,
                    (true, true) or (false, false) => value
                };
                if (block.MapVerticalMotion(value) != expected || block.MapIdentity(identity) != expectedIdentity)
                {
                    throw new InvalidDataException("Co-located vertical motion or reference identity differs from the picture structure.");
                }
            }
        }
    }
}

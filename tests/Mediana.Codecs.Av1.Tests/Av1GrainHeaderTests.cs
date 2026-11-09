// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1GrainHeaderTests
    {
        [Theory]
        [InlineData(true, true)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(false, false)]
        public void InheritsAvailableParametersWithTheNewSeedWithoutMutatingTheReference(bool applyGrain, bool showable)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            Av1GrainParameters? original = applyGrain
                ? new(17, new Av1GrainPoint[] { new(0, 32) }, ReadOnlyMemory<sbyte>.Empty, default, default, false, 8, 0, 6, 0, false, false)
                : null;
            Av1SequenceHeader geometry = default(Av1SequenceHeader) with { Width = 32, Height = 32, Monochrome = true, FilmGrain = true };
            Av1FrameHeader header = default(Av1FrameHeader) with
            {
                Geometry = geometry,
                Grain = original,
                FrameType = Av1FrameType.Key,
                RefreshReferences = 1,
                Showable = showable
            };
            using (Av1ReferencePictures references = new())
            using (WritableMediaBuffer storage = allocator.Allocate(geometry.SampleCount))
            using (Av1Picture picture = new(header, storage.Publish()))
            {
                references.Save(picture, new ushort[] { 1 }, allocator);
                Av1FrameContext context = default(Av1FrameContext) with { Type = Av1FrameType.Inter, ShowFrame = true, Geometry = geometry };
                Av1BitReader bits = new(Reference(65535, 0));
                Av1GrainParameters? inherited = Av1GrainHeader.Read(ref bits, context, references);
                if (applyGrain)
                {
                    Assert.Equal(65535, inherited!.Seed);
                    Assert.Equal(17, original!.Seed);
                    Assert.NotSame(original, inherited);
                    Assert.Equal(original.LumaPoints, inherited.LumaPoints);
                }
                else { Assert.Null(inherited); }
                if (showable)
                {
                    using Av1Picture shown = references.Show(0);
                    Assert.Same(original, shown.Header.Grain);
                }
                Assert.Same(original, references.Get(0).Header.Grain);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(false, true, true)]
        [InlineData(true, false, false)]
        public void AbsentSyntaxDoesNotConsumeBits(bool sequenceGrain, bool show, bool showable)
        {
            using Av1ReferencePictures references = new();
            Av1SequenceHeader geometry = default(Av1SequenceHeader) with { FilmGrain = sequenceGrain };
            Av1FrameContext context = default(Av1FrameContext) with { Geometry = geometry, ShowFrame = show, Showable = showable };
            Av1BitReader bits = new(new byte[] { 0xff });
            Assert.Null(Av1GrainHeader.Read(ref bits, context, references));
            Assert.Equal(8, bits.RemainingBits);
        }

        [Fact]
        public void RejectsUnselectedOrUninitializedGrainReferences()
        {
            using Av1ReferencePictures references = new();
            Av1SequenceHeader geometry = default(Av1SequenceHeader) with { FilmGrain = true };
            Av1FrameContext context = default(Av1FrameContext) with { Type = Av1FrameType.Inter, Geometry = geometry, ShowFrame = true };
            foreach (int index in new[] { 0, 3 })
            {
                Assert.Throws<InvalidDataException>(() =>
                {
                    Av1BitReader bits = new(Reference(19, index));
                    Av1GrainHeader.Read(ref bits, context, references);
                });
            }
        }

        private static byte[] Reference(int seed, int index)
        {
            int value = (1 << 20) | (seed << 4) | index;
            return [(byte)(value >> 13), (byte)(value >> 5), (byte)(value << 3)];
        }
    }
}

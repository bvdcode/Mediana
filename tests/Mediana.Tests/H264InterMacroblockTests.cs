using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class H264InterMacroblockTests
    {
        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 0)]
        [InlineData(2, 0)]
        [InlineData(3, 0)]
        [InlineData(3, 1)]
        [InlineData(3, 2)]
        [InlineData(3, 3)]
        [InlineData(4, 3)]
        public void ReadsAllPPartitionShapesAndReferenceIndices(int type, int subtype)
        {
            H264SliceHeader header = Header();
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264Picture target = new(header.Sequence, allocator);
            using H264Picture first = new(header.Sequence, allocator);
            using H264Picture second = new(header.Sequence, allocator);
            for (int plane = 0; plane < 3; plane++)
            {
                for (int index = 0; index < first.Plane(plane).Length; index++)
                {
                    first.Plane(plane)[index] = (byte)(index % 97);
                    second.Plane(plane)[index] = (byte)(128 + index % 97);
                }
            }
            int partitions = type == 0 ? 1 : type <= 2 ? 2 : 4;
            string syntax = Ue(type);
            if (partitions == 4)
            {
                syntax += string.Concat(Enumerable.Repeat(Ue(subtype), 4));
            }
            if (type != 4)
            {
                syntax += string.Concat(Enumerable.Range(0, partitions).Select(part => part % 2 == 0 ? "1" : "0"));
            }
            int subpartitions = partitions != 4 ? 1 : subtype == 0 ? 1 : subtype == 3 ? 4 : 2;
            syntax += new string('1', partitions * subpartitions * 2) + Ue(0);
            H264BitReader bits = new(H264TestBits.Rbsp(syntax));
            int qp = 26;
            H264InterMacroblock.Read(ref bits, target, header, [new(first, 0), new(second, 1)], 0, ref qp);
            bits.ReadTrailingBits();
            for (int plane = 0; plane < 3; plane++)
            {
                int size = plane == 0 ? 16 : 8;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        int part = type switch
                        {
                            0 => 0,
                            1 => y / (size / 2),
                            2 => x / (size / 2),
                            3 or 4 => y / (size / 2) * 2 + x / (size / 2),
                            _ => throw new InvalidOperationException()
                        };
                        H264Picture reference = type != 4 && part % 2 != 0 ? second : first;
                        Assert.Equal(reference.Plane(plane)[y * size + x], target.Plane(plane)[y * size + x]);
                    }
                }
                Assert.All(target.Counts(plane).ToArray(), count => Assert.Equal(0, count));
            }
            Assert.All(target.Modes.ToArray(), mode => Assert.Equal(H264Picture.InterMode, mode));
            Assert.Equal(26, target.Quantizers[0]);
        }

        [Fact]
        public void SkipAppliesReferenceWeightsWithoutReadingResidualSyntax()
        {
            H264SliceHeader header = Header() with
            {
                ActiveReferencesL0 = 1,
                Weights = new H264WeightTable(0, 0, [new(1, 2, 1, 3, 1, 4)])
            };
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264Picture reference = new(header.Sequence, allocator);
            using H264Picture target = new(header.Sequence, allocator);
            reference.Plane(0).Fill(80);
            reference.Plane(1).Fill(90);
            reference.Plane(2).Fill(100);
            H264BitReader bits = new(H264TestBits.Rbsp(string.Empty));
            int qp = 30;
            H264InterMacroblock.Read(ref bits, target, header, [new(reference, 0)], 0, ref qp, skip: true);
            bits.ReadTrailingBits();
            Assert.All(target.Plane(0).ToArray(), value => Assert.Equal(82, value));
            Assert.All(target.Plane(1).ToArray(), value => Assert.Equal(93, value));
            Assert.All(target.Plane(2).ToArray(), value => Assert.Equal(104, value));
            Assert.Equal(30, target.Quantizers[0]);
        }

        [Fact]
        public void UnavailableReferenceIsRejected()
        {
            H264SliceHeader header = Header();
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264Picture target = new(header.Sequence, allocator);
            Assert.Throws<InvalidDataException>(() =>
            {
                H264BitReader bits = new(H264TestBits.Rbsp("1 1 11 1"));
                int qp = 26;
                H264InterMacroblock.Read(ref bits, target, header, [null, null], 0, ref qp);
            });
        }

        private static string Ue(int value)
        {
            string binary = Convert.ToString(value + 1, 2);
            return new string('0', binary.Length - 1) + binary;
        }

        private static H264SliceHeader Header() => new(new H264SequenceParameterSet
        {
            WidthInMbs = 1,
            HeightInMapUnits = 1,
            FrameMbsOnly = true,
            Width = 16,
            Height = 16
        }, new H264PictureParameterSet(0, 0, false, false, 26, 0, 0, true, false, false, false, 2, 1, false, 0))
        {
            Kind = H264SliceKind.P,
            ActiveReferencesL0 = 2
        };
    }
}

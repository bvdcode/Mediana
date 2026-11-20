// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    public class H264FieldHeaderTests
    {
        [Theory]
        [InlineData("avc-paff-cabac.mp4", true)]
        [InlineData("avc-paff-cavlc.mp4", false)]
        public async Task ReferenceEncoderFieldHeadersMatchIndependentTrace(string file, bool cabac)
        {
            H264PictureOrderState order = new();
            for (int packet = 0; packet < 24; packet++)
            {
                (H264Configuration configuration, byte[] nal) = await H264TestInput.ReadSliceAsync(Mp4TestData.PathFor(file), packet);
                H264BitReader bits = H264Nal.Payload(nal, H264Nal.Type(nal));
                H264SliceHeader header = H264SliceHeaderReader.Read(ref bits, configuration, nal[0]);
                bool idr = packet % 8 == 0;
                Assert.False(header.Sequence.FrameMbsOnly);
                Assert.Equal(cabac, header.Picture.Cabac);
                Assert.Equal(packet % 2 == 0 ? H264PictureStructure.TopField : H264PictureStructure.BottomField, header.Structure);
                Assert.True(header.Layout.IsField);
                Assert.False(header.Layout.MacroblockAdaptiveFrameField);
                Assert.Equal(18, header.Layout.MacroblockCount);
                Assert.Equal(packet % 8 / 2, header.FrameNumber);
                Assert.Equal(packet % 8, header.PictureOrderLsb);
                Assert.Equal(0, header.DeltaPictureOrderBottom);
                Assert.Equal(0, header.FirstMacroblock);
                Assert.Equal(24, header.Qp);
                Assert.Equal(idr, header.IsIdr);
                Assert.Equal(idr ? H264SliceKind.I : H264SliceKind.P, header.Kind);
                Assert.True(bits.MoreRbspData());
                order = order.Advance(header);
                Assert.Equal(packet % 8, order.Current.Value);
                Assert.Equal(header.Structure, order.Current.Structure);
            }
        }

        [Theory]
        [InlineData(0, 0, 5, 2, 0, 0)]
        [InlineData(0, 1, 5, 0, 0, 0)]
        [InlineData(0, 2, 5, 0, 0, 0)]
        [InlineData(1, 0, 0, 0, -1, 2)]
        [InlineData(1, 1, 0, 0, -1, 0)]
        [InlineData(1, 2, 0, 0, -1, 0)]
        public void BottomFieldOrderSyntaxIsPresentOnlyInFramePictures(int orderType, int structureValue,
            int lsb, int bottom, int delta0, int delta1)
        {
            H264PictureStructure structure = (H264PictureStructure)structureValue;
            H264Configuration configuration = Configuration(orderType);
            H264BitReader bits = new(Payload(orderType, structure));
            H264SliceHeader header = H264SliceHeaderReader.Read(ref bits, configuration, 1);
            bits.ReadTrailingBits();
            Assert.Equal(structure, header.Structure);
            Assert.Equal(lsb, header.PictureOrderLsb);
            Assert.Equal(bottom, header.DeltaPictureOrderBottom);
            Assert.Equal(delta0, header.DeltaPictureOrder0);
            Assert.Equal(delta1, header.DeltaPictureOrder1);
            Assert.Equal(26, header.Qp);
        }

        [Fact]
        public void ComplementaryFieldsAreSeparatePrimaryPictures()
        {
            byte[] top = [1, .. Payload(0, H264PictureStructure.TopField)];
            byte[] bottom = [1, .. Payload(0, H264PictureStructure.BottomField)];
            H264AccessUnit unit = new(Configuration(0), new ReadOnlyMemory<byte>[] { top, bottom }, false, false);
            Assert.Throws<InvalidDataException>(() => H264SliceHeaders.Read(unit, new H264DecoderOptions(), default));
        }

        private static H264Configuration Configuration(int orderType)
        {
            H264SequenceParameterSet sequence = new()
            {
                FrameNumberBits = 4,
                PictureOrderType = orderType,
                PictureOrderBits = 4,
                WidthInMbs = 6,
                HeightInMapUnits = 3
            };
            H264PictureParameterSet picture = new(0, 0, false, true, 26, 0, 0, false, false, false, false, 1, 1, false, 0);
            return new H264Configuration(4, new Dictionary<int, H264SequenceParameterSet> { [0] = sequence },
                new Dictionary<int, H264PictureParameterSet> { [0] = picture });
        }

        private static byte[] Payload(int orderType, H264PictureStructure structure)
        {
            string fieldSyntax = structure switch
            {
                H264PictureStructure.Frame => "0",
                H264PictureStructure.TopField => "10",
                H264PictureStructure.BottomField => "11",
                _ => throw new ArgumentOutOfRangeException(nameof(structure))
            };
            string orderSyntax = orderType switch
            {
                0 => "0101",
                1 => "011",
                _ => throw new ArgumentOutOfRangeException(nameof(orderType))
            };
            if (structure == H264PictureStructure.Frame)
            {
                orderSyntax += "00100";
            }
            return H264TestBits.Rbsp("1 011 1 0010 " + fieldSyntax + orderSyntax + "1");
        }
    }
}

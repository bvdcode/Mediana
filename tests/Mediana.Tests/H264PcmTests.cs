// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text;
using System.Runtime.InteropServices;
using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class H264PcmTests
    {
        [Theory]
        [InlineData(false, 1, 8)]
        [InlineData(false, 1, 10)]
        [InlineData(false, 2, 8)]
        [InlineData(false, 2, 10)]
        [InlineData(true, 1, 8)]
        [InlineData(true, 1, 10)]
        [InlineData(true, 2, 8)]
        [InlineData(true, 2, 10)]
        public void PcmPreservesAllSamplesAndChecksAlignment(bool invalidAlignment, int chromaFormat, int bitDepth)
        {
            H264SequenceParameterSet sequence = new()
            {
                Id = 0,
                Profile = chromaFormat == 1 ? 66 : 122,
                ChromaFormat = chromaFormat,
                LumaBitDepth = bitDepth,
                ChromaBitDepth = bitDepth,
                FrameNumberBits = 4,
                PictureOrderType = 2,
                WidthInMbs = 1,
                HeightInMapUnits = 1,
                FrameMbsOnly = true,
                Width = 16,
                Height = 16
            };
            H264PictureParameterSet picture = new(Id: 0, SequenceId: 0, Cabac: false,
                BottomFieldPictureOrderPresent: false, InitialQp: 26, ChromaQpOffset: 0, SecondChromaQpOffset: 0,
                DeblockingControlPresent: true, ConstrainedIntraPrediction: false, RedundantPictureCountPresent: false,
                Transform8x8: false, DefaultReferencesL0: 1, DefaultReferencesL1: 1, WeightedPrediction: false, WeightedBiPrediction: 0);
            H264Configuration configuration = new(4, new Dictionary<int, H264SequenceParameterSet> { [0] = sequence },
                new Dictionary<int, H264PictureParameterSet> { [0] = picture });
            // IDR I slice, QP 26, deblocking disabled, I_PCM macroblock and seven alignment bits.
            StringBuilder syntax = new("1011100001001010000011010");
            syntax.Append(invalidAlignment ? "1000000" : "0000000");
            int chromaSamples = 64 * chromaFormat;
            int sampleBytes = bitDepth > 8 ? 2 : 1;
            byte[] expected = new byte[(256 + 2 * chromaSamples) * sampleBytes];
            for (int index = 0; index < 256 + 2 * chromaSamples; index++)
            {
                int value = index % 8 < 4 ? 0 : index * 3 % (1 << bitDepth);
                if (sampleBytes == 1)
                {
                    expected[index] = (byte)value;
                }
                else
                {
                    MemoryMarshal.Cast<byte, ushort>(expected)[index] = (ushort)value;
                }
                syntax.Append(Convert.ToString(value, 2).PadLeft(bitDepth, '0'));
            }
            byte[] nal = [0x65, .. H264TestBits.Rbsp(syntax.ToString())];
            MediaBufferAllocator allocator = new(1024 * 1024);
            if (invalidAlignment)
            {
                Assert.Throws<InvalidDataException>(() => H264IntraDecoder.Decode(nal, configuration, allocator));
            }
            else
            {
                using H264Picture actual = H264IntraDecoder.Decode(nal, configuration, allocator);
                Assert.Equal(expected[..(256 * sampleBytes)], actual.Plane(0).ToArray());
                Assert.Equal(expected[(256 * sampleBytes)..((256 + chromaSamples) * sampleBytes)], actual.Plane(1).ToArray());
                Assert.Equal(expected[((256 + chromaSamples) * sampleBytes)..], actual.Plane(2).ToArray());
                Assert.All(actual.Counts(0).ToArray(), count => Assert.Equal(16, count));
                Assert.All(actual.Modes.ToArray(), mode => Assert.Equal(2, mode));
                Assert.Equal(6 * (bitDepth - 8), actual.Quantizers[0]);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}

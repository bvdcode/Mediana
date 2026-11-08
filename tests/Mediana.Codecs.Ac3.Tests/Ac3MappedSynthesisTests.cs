// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Ac3.Tests
{
    public class Ac3MappedSynthesisTests
    {
        [Theory]
        [InlineData(2)]
        [InlineData(8)]
        [InlineData(16)]
        public void DirectMappedSynthesisPreservesOtherChannelsAndUsesTheFinalInterleavedStride(int channels)
        {
            byte[] data = Ac3SyntheticFrame.Create(true, false, 3);
            Ac3FrameHeader header = Ac3FrameHeader.Read(data);
            float[] expected = new float[768], actual = Enumerable.Repeat(.125f, 768 * channels).ToArray();
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (Ac3FrameDecoder decoder = new(allocator, false)) { decoder.Decode(data, header, expected, default); }
            using (Ac3FrameDecoder decoder = new(allocator, false))
            { decoder.DecodeMapped(data, header, actual, channels, new[] { channels - 1 }, default, default); }
            for (int sample = 0; sample < 768; sample++)
            {
                for (int channel = 0; channel < channels; channel++)
                { Assert.Equal(channel == channels - 1 ? expected[sample] : .125f, actual[sample * channels + channel]); }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void ProgrammeDynamicsReplaceLocalGainBeforeTransformOverlap()
        {
            byte[] data = Ac3SyntheticFrame.Create(true, true, dynamicRange: 0xe0);
            Ac3FrameHeader header = Ac3FrameHeader.Read(data);
            float[] expected = new float[1536], actual = new float[1536];
            double[] programmeGain = Enumerable.Repeat(2d, 12).ToArray();
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (Ac3FrameDecoder decoder = new(allocator, false)) { decoder.Decode(data, header, expected, default); }
            using (Ac3FrameDecoder decoder = new(allocator))
            {
                decoder.DecodeMapped(data, header, actual, 1, default, programmeGain, default);
                Assert.True(decoder.DynamicRangeMetadataSeen); Assert.Equal(.5, decoder.FrameDynamics[0]);
            }
            for (int sample = 0; sample < 1536; sample++) { Assert.Equal(expected[sample] * 2, actual[sample]); }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void RecoveryTracksDependenciesAboveTheFirstEightProgrammeChannels()
        {
            string[] labels = Enumerable.Range(0, 16).Select(channel => $"discrete-{channel}").ToArray();
            AudioFormat format = new(48000, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved, labels);
            MediaBufferAllocator allocator = new(1024 * 1024);
            int[] locations = Enumerable.Repeat(-1, 16).ToArray(), lengths = new int[16];
            locations[14] = 1536; lengths[14] = 255;
            using (Ac3TransientProcessing history = new(allocator, format, false))
            {
                Assert.Throws<NotSupportedException>(() =>
                {
                    for (int frame = 0; frame < 52; frame++)
                    {
                        history.BeginAccessUnit(256, frame < 26 ? 0 : 256, new(frame * 256, new(1, 48000)));
                        history.GetFrameStorage(256).Fill(.01f);
                        history.AppendFrame(256, locations, lengths);
                        while (history.HasOutput) { history.Receive().Dispose(); }
                    }
                });
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}

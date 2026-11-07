// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text.Json;
using Mediana.Core.Memory;
using Mediana.Formats.Audio;

namespace Mediana.Containers.Ogg.Tests
{
    public class OggAudioFormatTests
    {
        [Fact]
        public async Task OpusIdentificationCommentsAndEveryCodedDurationMatchIndependentDemuxing()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using OggTestSource source = new(await File.ReadAllBytesAsync(DataPath("opus-stereo.ogg")));
            await using OggPacketReader reader = new(source, allocator);
            using OggPacket identification = (await reader.ReadPacketAsync())!;
            OpusConfiguration configuration = OpusConfiguration.Read(identification.Data.Span);
            Assert.Equal(2, configuration.ChannelCount);
            Assert.Equal(312, configuration.PreSkipSamples);
            Assert.Equal(48000u, configuration.InputSampleRate);
            Assert.Equal(new byte[] { 0, 1 }, configuration.ChannelMapping.ToArray());
            using OggPacket comments = (await reader.ReadPacketAsync())!;
            Assert.True(comments.Data.Span.StartsWith("OpusTags"u8));
            VorbisComments tags = VorbisComments.Read(comments.Data.Span[8..], out int consumed);
            Assert.True(consumed <= comments.Data.Length - 8);
            Assert.Contains(tags.Tags, tag => tag.Name == "title" && tag.Values[0] == "Ogg timing — тест");
            using JsonDocument timing = JsonDocument.Parse(await File.ReadAllBytesAsync(DataPath("opus-stereo.timing.json")));
            int index = 0;
            long next = -configuration.PreSkipSamples;
            while (await reader.ReadPacketAsync() is OggPacket packet)
            {
                using (packet)
                {
                    JsonElement expected = timing.RootElement[index++];
                    OpusPacketInfo info = OpusPacketInfo.Read(packet.Data.Span);
                    Assert.Equal(next, expected.GetProperty("pts").GetInt64());
                    if (!packet.IsEnd) { Assert.Equal(info.SampleCount, expected.GetProperty("duration").GetInt64()); }
                    else { Assert.Equal(expected.GetProperty("pts").GetInt64() + expected.GetProperty("duration").GetInt64(), packet.GranulePosition - configuration.PreSkipSamples); }
                    next += info.SampleCount;
                }
            }
            Assert.Equal(timing.RootElement.GetArrayLength(), index);
        }

        [Fact]
        public async Task VorbisSetupModesAndEveryOverlapIntervalMatchIndependentDemuxing()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using OggTestSource source = new(await File.ReadAllBytesAsync(DataPath("vorbis-stereo.ogg")));
            await using OggPacketReader reader = new(source, allocator);
            using OggPacket identification = (await reader.ReadPacketAsync())!;
            VorbisIdentification configuration = VorbisIdentification.Read(identification.Data.Span);
            Assert.Equal(2, configuration.ChannelCount);
            Assert.Equal(44100, configuration.SampleRate);
            Assert.Equal(256, configuration.SmallBlockSize);
            Assert.Equal(2048, configuration.LargeBlockSize);
            using OggPacket comments = (await reader.ReadPacketAsync())!;
            VorbisComments tags = VorbisComments.Read(comments.Data.Span[7..], out int consumed);
            Assert.Equal(1, comments.Data.Span[7 + consumed] & 1);
            Assert.Contains(tags.Tags, tag => tag.Name == "title" && tag.Values[0] == "Vorbis timing — тест");
            using OggPacket setupPacket = (await reader.ReadPacketAsync())!;
            VorbisSetupInfo setup = VorbisSetupInfo.Read(configuration, setupPacket.Data.Span);
            using JsonDocument timing = JsonDocument.Parse(await File.ReadAllBytesAsync(DataPath("vorbis-stereo.timing.json")));
            int previous = 0, index = 0;
            long next = 0;
            while (await reader.ReadPacketAsync() is OggPacket packet)
            {
                using (packet)
                {
                    JsonElement expected = timing.RootElement[index++];
                    int block = setup.GetBlockSize(packet.Data.Span);
                    int duration = previous == 0 ? block / 2 : (previous + block) / 4;
                    if (previous == 0) { next = -duration; }
                    Assert.Equal(next, expected.GetProperty("pts").GetInt64());
                    if (!packet.IsEnd) { Assert.Equal(duration, expected.GetProperty("duration").GetInt64()); }
                    else { Assert.Equal(next + expected.GetProperty("duration").GetInt64(), packet.GranulePosition); }
                    next += duration;
                    previous = block;
                }
            }
            Assert.Equal(timing.RootElement.GetArrayLength(), index);
        }

        [Fact]
        public void EveryOpusTocConfigurationHasTheSpecifiedDuration()
        {
            int[] durations = [480, 960, 1920, 2880, 480, 960, 1920, 2880, 480, 960, 1920, 2880,
                480, 960, 480, 960, 120, 240, 480, 960, 120, 240, 480, 960, 120, 240, 480, 960, 120, 240, 480, 960];
            for (int configuration = 0; configuration < 32; configuration++)
            {
                foreach (int stereo in new[] { 0, 4 })
                {
                    byte toc = (byte)((configuration << 3) | stereo);
                    Assert.Equal(durations[configuration], OpusPacketInfo.Read([toc]).SampleCount);
                    Assert.Equal(2 * durations[configuration], OpusPacketInfo.Read([(byte)(toc | 1)]).SampleCount);
                    Assert.Equal(2 * durations[configuration], OpusPacketInfo.Read([(byte)(toc | 2), 0]).SampleCount);
                    int frames = 5760 / durations[configuration];
                    Assert.Equal(frames * durations[configuration], OpusPacketInfo.Read([(byte)(toc | 3), (byte)frames]).SampleCount);
                }
            }
        }

        [Fact]
        public void OpusVbrPaddingAndMaximumFrameLengthAreValidatedWithoutAllocating()
        {
            byte[] valid = [0x83, 0xc3, 2, 2, 3, 1, 2, 3, 4, 5, 6, 0, 0];
            Assert.Equal(360, OpusPacketInfo.Read(valid).SampleCount);
            byte[] maximum = new byte[3 + 1275 + 5];
            maximum[0] = 0x82;
            maximum[1] = maximum[2] = 255;
            Assert.Equal(240, OpusPacketInfo.Read(maximum).SampleCount);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 1000; index++) { OpusPacketInfo.Read(valid); }
            Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        }

        [Theory]
        [InlineData(new byte[] { })]
        [InlineData(new byte[] { 0x81, 1 })]
        [InlineData(new byte[] { 0x82 })]
        [InlineData(new byte[] { 0x82, 252 })]
        [InlineData(new byte[] { 0x82, 3, 1 })]
        [InlineData(new byte[] { 0x83 })]
        [InlineData(new byte[] { 0x83, 0 })]
        [InlineData(new byte[] { 0x83, 49 })]
        [InlineData(new byte[] { 0x83, 0x41, 255 })]
        [InlineData(new byte[] { 0x83, 0x83, 1, 2, 1 })]
        public void InvalidOpusFramingFailsExplicitly(byte[] packet)
            => Assert.Throws<InvalidDataException>(() => OpusPacketInfo.Read(packet));

        [Fact]
        public void OpusMappedChannelsPreserveSilenceSlotsAndGainAndRejectAbsentChannels()
        {
            byte[] header = new byte[27];
            "OpusHead"u8.CopyTo(header);
            header[8] = 1;
            header[9] = 6;
            header[18] = 1;
            header[19] = 4;
            header[20] = 2;
            new byte[] { 0, 4, 1, 2, 3, 255 }.CopyTo(header, 21);
            BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(16), -384);
            OpusConfiguration configuration = OpusConfiguration.Read(header);
            Assert.Equal(-1.5, configuration.OutputGainDecibels);
            Assert.Equal(4, configuration.StreamCount);
            Assert.Equal(2, configuration.CoupledStreamCount);
            Assert.Equal(255, configuration.ChannelMapping.Span[5]);
            header[26] = 6;
            Assert.Throws<InvalidDataException>(() => OpusConfiguration.Read(header));
        }

        private static string DataPath(string name) => Path.Combine(AppContext.BaseDirectory, "Data", name);
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Opus;
using Mediana.Containers;
using Mediana.Containers.Matroska;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Timing;
using Mediana.IO;
using Mediana.Processing.Audio;
using static Mediana.Tests.MatroskaIndexTestData;
using static Mediana.Tests.MatroskaMetadataTestData;
using static Mediana.Tests.MatroskaReaderTestData;

namespace Mediana.Tests
{
    public class MatroskaOpusTests
    {
        private static readonly TimeBase opusClock = new(1, 48000);

        [Fact]
        public async Task WebmDecodesThroughEndAndBuildsAWaveform()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Data", "Matroska", "opus-mono.webm");
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new MatroskaReaderFactory())
                .AddAudioDecoder(new OpusDecoderFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(path));
            MediaInfo probe = await engine.ProbeAsync(source);
            Assert.Equal("opus", Assert.Single(probe.Streams).CodecId);
            AudioStreamMetadata metadata = Assert.IsType<AudioStreamMetadata>(probe.StreamMetadata[0]);
            Assert.Equal(48000, metadata.SampleRate);
            Assert.Equal(1, metadata.ChannelCount);

            await using (AudioSession session = await engine.OpenAudioAsync(source, 0))
            {
                long samples = 0;
                while (await session.ReadFrameAsync() is AudioFrame frame)
                {
                    using (frame)
                    {
                        Assert.Equal(new MediaTimestamp(samples, opusClock), frame.Timestamp);
                        samples += frame.SamplesPerChannel;
                    }
                }
                Assert.Equal(48816, samples);
            }

            await using (AudioSession session = await engine.OpenAudioAsync(source, 0))
            {
                MediaBufferAllocator allocator = new(1024 * 1024);
                await using AudioWaveformReader waveform = new(session.ReadFrameAsync, [1], new AudioWaveformOptions(400, 40), allocator);
                int samples = 0;
                double peak = 0;
                while (await waveform.ReadLevelAsync() is AudioLevel level)
                {
                    samples += level.SampleCount;
                    peak = Math.Max(peak, level.Peak);
                }
                Assert.Equal(407, samples);
                Assert.True(peak > 0.05);
            }
            await using MatroskaReader reader = await MatroskaReader.OpenAsync(source, new MediaBufferAllocator(1024 * 1024));
            ContainerSeekPoint preroll = reader.GetPrerollStart(0, 10, 0);
            Assert.Equal(6, preroll.PacketIndex);
        }

        [Fact]
        public async Task TrailingZeroPaddingRequiresExplicitCompatibilityAndRejectsNonzeroData()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Data", "Matroska", "opus-mono.webm");
            byte[] original = await File.ReadAllBytesAsync(path);
            const int padding = 9000;
            byte[] data = new byte[original.Length + padding];
            original.CopyTo(data, 0);
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using RandomAccessStreamByteSource strict = new(new MemoryStream(data));
            await Assert.ThrowsAsync<InvalidDataException>(async () => await MatroskaReader.OpenAsync(strict, allocator));

            List<string> warnings = [];
            MatroskaReadOptions options = new() { AllowTrailingZeroPadding = true, WarningHandler = warnings.Add };
            await using RandomAccessStreamByteSource allowed = new(new MemoryStream(data));
            await using (MatroskaReader reader = await MatroskaReader.OpenAsync(allowed, allocator, options))
            {
                Assert.Equal("opus", Assert.Single(reader.Streams).CodecId);
                using MediaPacket packet = Assert.IsType<MediaPacket>(await reader.ReadPacketAsync());
                Assert.NotEmpty(packet.Data.ToArray());
            }
            Assert.Contains(padding.ToString(System.Globalization.CultureInfo.InvariantCulture), Assert.Single(warnings));

            data[^1] = 1;
            await using RandomAccessStreamByteSource corrupt = new(new MemoryStream(data));
            await Assert.ThrowsAsync<InvalidDataException>(async () => await MatroskaReader.OpenAsync(corrupt, allocator, options));
            Assert.Single(warnings);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task VariableOpusLaceSpreadsEndPaddingAcrossItsPackets()
        {
            byte[] track = Field(0xAE, [.. Unsigned(0xD7, 1), .. Unsigned(0x73C5, 2), .. Unsigned(0x83, 2),
                .. Field(0x86, "A_OPUS"u8), .. Field(0x63A2, Convert.FromHexString("4F707573486561640101000080BB0000000000")),
                .. Field(0xE1, Float(0xB5, 48000))]);
            byte[] group = Field(0xA0, [.. Block(0xA1, flags: 2, payload: [1, 1, 0xf8, 0xf0]), .. Signed(0x75A2, 12_000_000)]);
            byte[] file = Document(Field(0x1549A966, InfoFields), Field(0x1654AE6B, track),
                Cluster(Unsigned(0xE7, 0), group));
            await using RandomAccessStreamByteSource source = new(new MemoryStream(file));
            await using MatroskaReader reader = await MatroskaReader.OpenAsync(source, new MediaBufferAllocator(1024 * 1024));
            using MediaPacket first = Assert.IsType<MediaPacket>(await reader.ReadPacketAsync());
            using MediaPacket second = Assert.IsType<MediaPacket>(await reader.ReadPacketAsync());
            Assert.Equal(new MediaTimestamp(0, opusClock), first.PresentationTime);
            Assert.Equal(new MediaDuration(864, opusClock), first.Duration);
            Assert.Equal(new MediaTimestamp(960, opusClock), second.PresentationTime);
            Assert.Equal(new MediaDuration(0, opusClock), second.Duration);
        }
    }
}

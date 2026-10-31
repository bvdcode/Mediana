// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Containers.Mp4;
using Mediana.Core.Memory;
using Mediana.Formats.Audio;

namespace Mediana.Tests
{
    public class Mp4SoundDescriptionTests
    {
        [Theory]
        [InlineData(48000)]
        [InlineData(96000)]
        [InlineData(44100)]
        public async Task Version2UsesExtendedDeclarationsAndDefinitiveCodecConfiguration(int declaredRate)
        {
            byte[] file = await Mp4TestData.ReadAsync("avc-sound-v2.mov");
            int payload = Mp4TestData.PayloadOffset(file, "mp4a");
            double rate = declaredRate;
            BinaryPrimitives.WriteInt64BigEndian(file.AsSpan(payload + 32), BitConverter.DoubleToInt64Bits(rate));
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(file, 7);
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator))
            {
                Mp4TrackInfo audio = Assert.Single(reader.Tracks, track => track.Stream.CodecId == "aac");
                Assert.Equal((int)rate, audio.DeclaredAudioSampleRate);
                Assert.Equal(1, audio.DeclaredAudioChannels);
                AacConfiguration? configuration = AacConfiguration.ReadAudioSpecificConfig(audio.Stream.Configuration.Span, out string? reason);
                Assert.Null(reason);
                Assert.NotNull(configuration);
                Assert.Equal(48000, configuration.SampleRate);
                Assert.Equal(1, configuration.ChannelCount);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(16, 0)]
        [InlineData(18, 0)]
        [InlineData(20, 0)]
        [InlineData(22, 1)]
        public async Task Version2ValidatesReservedLegacyConstants(int offset, ushort value)
        {
            byte[] file = await Mp4TestData.ReadAsync("avc-sound-v2.mov");
            BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(Mp4TestData.PayloadOffset(file, "mp4a") + offset), value);
            await RejectAsync<InvalidDataException>(file);
        }

        [Theory]
        [InlineData(28, 71)]
        [InlineData(28, 76)]
        [InlineData(48, 16)]
        [InlineData(52, 1)]
        [InlineData(56, 1)]
        [InlineData(40, 0)]
        [InlineData(40, uint.MaxValue)]
        public async Task UnsupportedVersion2StructuresFailWithoutRetainingStorage(int offset, uint value)
        {
            byte[] file = await Mp4TestData.ReadAsync("avc-sound-v2.mov");
            BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(Mp4TestData.PayloadOffset(file, "mp4a") + offset), value);
            await RejectAsync<NotSupportedException>(file);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-48000)]
        [InlineData(48000.5)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(2147483648d)]
        public async Task UnsupportedVersion2RatesAreExplicit(double rate)
        {
            byte[] file = await Mp4TestData.ReadAsync("avc-sound-v2.mov");
            BinaryPrimitives.WriteInt64BigEndian(file.AsSpan(Mp4TestData.PayloadOffset(file, "mp4a") + 32), BitConverter.DoubleToInt64Bits(rate));
            await RejectAsync<NotSupportedException>(file);
        }

        [Fact]
        public Task Complete96KhzAacWithZeroContainerRateMatchesIndependentPcm() =>
            HighRateAacTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Aac"));

        [Theory]
        [InlineData(0)]
        [InlineData(44100)]
        public async Task ContainerSampleRateDoesNotReplaceDefinitiveAacConfiguration(int declaredRate)
        {
            byte[] file = await Mp4TestData.ReadAsync("avc-aac.mp4");
            int payload = Mp4TestData.PayloadOffset(file, "mp4a");
            BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(payload + 24), (uint)declaredRate << 16);
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(file, 7);
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator))
            {
                Mp4TrackInfo audio = Assert.Single(reader.Tracks, track => track.Stream.CodecId == "aac");
                Assert.Equal(declaredRate, audio.DeclaredAudioSampleRate);
                AacConfiguration? configuration = AacConfiguration.ReadAudioSpecificConfig(audio.Stream.Configuration.Span, out string? reason);
                Assert.Null(reason);
                Assert.NotNull(configuration);
                Assert.Equal(48000, configuration.SampleRate);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task CompleteQuickTimeAacMatchesIndependentTimingAndNoiseEnergy() =>
            await QuickTimeAacTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Metadata"));

        [Fact]
        public async Task RealQuickTimeAacReadsWaveConfigurationAndRedefinedPackets()
        {
            byte[] file = await ReadAsync();
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(file, 7);
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator))
            {
                Assert.Equal(3, reader.Tracks.Count);
                Assert.Equal("aac", reader.Tracks[1].Stream.CodecId);
                Assert.Equal(48000, reader.Tracks[1].DeclaredAudioSampleRate);
                Assert.True(reader.Tracks[1].SampleCount > 0);
                Assert.NotEmpty(reader.Tracks[1].Stream.Configuration.ToArray());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(0xffff)]
        public async Task LegacyUncompressedSampleFictionIsNotTreatedAsCompressedPackets(int compressionId)
        {
            byte[] file = await ReadAsync();
            int payload = Mp4TestData.PayloadOffset(file, "mp4a");
            Assert.Equal(1, BinaryPrimitives.ReadUInt16BigEndian(file.AsSpan(payload + 8)));
            BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(payload + 20), (ushort)compressionId);
            await RejectAsync<NotSupportedException>(file);
        }

        [Theory]
        [InlineData(32)]
        [InlineData(36)]
        public async Task ReservedVariableRatePacketSizesAreValidated(int fieldOffset)
        {
            byte[] file = await ReadAsync();
            int payload = Mp4TestData.PayloadOffset(file, "mp4a");
            BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(payload + fieldOffset), 1);
            await RejectAsync<InvalidDataException>(file);
        }

        [Fact]
        public async Task OriginalWaveFormatMustMatchTheOuterSampleEntry()
        {
            byte[] file = await ReadAsync();
            "alac"u8.CopyTo(file.AsSpan(Mp4TestData.PayloadOffset(file, "frma"), 4));
            await RejectAsync<InvalidDataException>(file);
        }

        [Fact]
        public async Task UnsupportedSoundDescriptionVersionIsExplicit()
        {
            byte[] file = await ReadAsync();
            BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(Mp4TestData.PayloadOffset(file, "mp4a") + 8), 3);
            await RejectAsync<NotSupportedException>(file);
        }

        private static async Task<byte[]> ReadAsync() => await File.ReadAllBytesAsync(
            Path.Combine(AppContext.BaseDirectory, "Data", "Metadata", "header-dates-future.mov"));

        private static async Task RejectAsync<T>(byte[] file) where T : Exception
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(file, 7);
            await Assert.ThrowsAsync<T>(async () => await Mp4Reader.OpenAsync(source, allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.IsDisposed);
        }
    }
}

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
            BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(Mp4TestData.PayloadOffset(file, "mp4a") + 8), 2);
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

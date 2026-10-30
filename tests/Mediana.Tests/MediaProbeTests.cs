// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using System.Text.Json;
using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Tests
{
    public class MediaProbeTests
    {
        public static IEnumerable<object[]> Cases => Enumerable.Range(0, MediaProbeTestInput.Cases.Length).Select(index => new object[] { index });

        [Theory]
        [MemberData(nameof(Cases))]
        public async Task HeaderMetadataMatchesIndependentValuesWithoutAnyDecoder(int index) =>
            await MediaProbeTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data"), index);

        [Fact]
        public async Task ProbeReplaysSequentialInputDisposesTheReaderAndNeverReadsPackets()
        {
            FixtureContainerFactory factory = new();
            MediaEngine engine = new MediaEngineBuilder().AddContainer(factory).Build();
            await using ShortReadSource source = new([.. "TEST"u8, 11, 22]);
            MediaInfo info = await engine.ProbeAsync(source);
            Assert.Equal("fixture", info.Container.FormatId);
            Assert.Equal("fixture.rgba", Assert.Single(info.Streams).CodecId);
            Assert.Null(info.Container.DeclaredDuration);
            Assert.True(factory.LastReader!.IsDisposed);
            Assert.Equal(0, factory.LastReader.PacketReads);
            Assert.False(source.IsDisposed);
            IList<EncodedStreamInfo> streams = Assert.IsAssignableFrom<IList<EncodedStreamInfo>>(info.Streams);
            Assert.Throws<NotSupportedException>(() => streams[0] = info.Streams[0]);
        }

        [Fact]
        public async Task RandomAccessCursorAndOwnedConfigurationSurviveProbeAndSourceDisposal()
        {
            byte[] format = WaveTestInput.Format(16, 2, 44100);
            byte[] bytes = WaveTestInput.File(("fmt ", format), ("data", new byte[4]));
            MediaInfo info;
            using MemoryStream memory = new(bytes);
            await using (RandomAccessStreamByteSource source = new(memory, leaveOpen: true))
            {
                await source.ReadAsync(new byte[3]);
                info = await MediaProbeTestInput.CreateEngine().ProbeAsync(source);
                Assert.Equal(3, memory.Position);
                Assert.True(memory.CanRead);
            }
            Assert.Equal(format, info.Streams[0].Configuration.ToArray());
            Assert.Equal(new MediaDuration(1, new TimeBase(1, 44100)), info.Container.DeclaredDuration);
        }

        [Theory]
        [InlineData("avc-aac.mp4")]
        [InlineData("avc-tail.mov")]
        public async Task Mp4ProbeDoesNotReadOrValidateEncodedPayloads(string file)
        {
            byte[] data = await Mp4TestData.ReadAsync(file);
            using JsonDocument reference = JsonDocument.Parse(await File.ReadAllBytesAsync(Mp4TestData.PathFor(file + ".json")));
            JsonElement[] packets = reference.RootElement.GetProperty("packets").EnumerateArray().ToArray();
            long firstPayload = packets.Min(packet => long.Parse(packet.GetProperty("pos").GetString()!, CultureInfo.InvariantCulture));
            long payloadEnd = packets.Max(packet => long.Parse(packet.GetProperty("pos").GetString()!, CultureInfo.InvariantCulture)
                + int.Parse(packet.GetProperty("size").GetString()!, CultureInfo.InvariantCulture));
            data.AsSpan((int)firstPayload, (int)(payloadEnd - firstPayload)).Clear();
            await using TrackingRandomAccessSource source = new(data, 7);
            MediaInfo info = await MediaProbeTestInput.CreateEngine().ProbeAsync(source);
            Assert.Equal("mp4", info.Container.FormatId);
            Assert.All(source.Reads, read => Assert.True(read.Offset + read.Length <= firstPayload || read.Offset >= payloadEnd));
            Assert.False(source.IsDisposed);
        }

        [Fact]
        public async Task UnknownMp3ExtentIsNotEstimatedFromFileLengthOrReadToEof()
        {
            byte[] frame = Mp3TestInput.Frame();
            await using TrackingRandomAccessSource source = new([.. frame, .. new byte[1024 * 1024]]);
            MediaInfo info = await MediaProbeTestInput.CreateEngine().ProbeAsync(source);
            Assert.Equal("mp3", info.Container.FormatId);
            Assert.Null(info.Container.DeclaredDuration);
            Assert.Null(Assert.Single(info.Streams).CodedDuration);
            Assert.All(source.Reads, read => Assert.True(read.Offset + read.Length <= frame.Length));
        }

        [Fact]
        public async Task UnknownAndZeroDurationRemainDistinct()
        {
            await using ShortReadSource unknown = new(FlacTestInput.File([0], (0, FlacTestInput.StreamInfo(samples: 0))));
            MediaInfo flac = await MediaProbeTestInput.CreateEngine().ProbeAsync(unknown);
            Assert.Null(flac.Container.DeclaredDuration);
            await using ShortReadSource empty = new(WaveTestInput.File(("fmt ", WaveTestInput.Format()), ("data", [])));
            MediaInfo wave = await MediaProbeTestInput.CreateEngine().ProbeAsync(empty);
            Assert.Equal(0, wave.Container.DeclaredDuration!.Value.Value);
        }

        [Fact]
        public async Task CancellationAndLimitsApplyBeforeAnyPacketAccess()
        {
            await using TrackingRandomAccessSource source = new([.. "TEST"u8, 11]);
            FixtureContainerFactory factory = new();
            MediaEngine engine = new MediaEngineBuilder().AddContainer(factory).Build();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await engine.ProbeAsync(source, cancellationToken: new CancellationToken(true)));
            Assert.Empty(source.Reads);
            await Assert.ThrowsAsync<NotSupportedException>(async () => await engine.ProbeAsync(source, new MediaOpenOptions { MaxProbeBytes = 3 }));
            await Assert.ThrowsAsync<MediaMemoryLimitException>(async () => await engine.ProbeAsync(source, new MediaOpenOptions { BufferBudgetBytes = 3 }));
            Assert.Empty(source.Reads);
            Assert.Null(factory.LastReader);
            Assert.False(source.IsDisposed);
        }

        [Fact]
        public void ContainerInfoValidatesItsIdentityAndExactTimeBase()
        {
            Assert.Throws<ArgumentException>(() => new ContainerInfo(" "));
            Assert.Throws<ArgumentException>(() => new ContainerInfo("test", default(MediaDuration)));
            Assert.Null(new ContainerInfo("test").DeclaredDuration);
        }
    }
}

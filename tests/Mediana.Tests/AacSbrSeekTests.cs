// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Codecs;
using Mediana.Codecs.Aac;
using Mediana.Containers;
using Mediana.Containers.Adts;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Execution;

namespace Mediana.Tests
{
    public class AacSbrSeekTests
    {
        [Theory]
        [InlineData(1, false, false)]
        [InlineData(2, false, false)]
        [InlineData(1, true, false)]
        [InlineData(2, true, false)]
        [InlineData(1, false, true)]
        [InlineData(2, false, true)]
        [InlineData(1, true, true)]
        [InlineData(2, true, true)]
        public async Task ColdAndRepeatedSeeksPreserveRequestedClocksAndIndependentRestartPcm(int channels, bool explicitSbr, bool mp4)
        {
            byte[][] packets = await AacSbrDecodeTestInput.ReadPacketsAsync(Root, channels);
            byte[] continuous = await AacSbrDecodeTestInput.ReadPcmAsync(Root, channels);
            byte[] restart = await AacSbrDecodeTestInput.ReadPcmAsync(Root, channels, true);
            string stem = channels switch { 1 => "transients-mono", 2 => "transients-stereo", _ => throw new ArgumentOutOfRangeException(nameof(channels)) };
            byte[] input = await File.ReadAllBytesAsync(Path.Combine(Root, stem + (mp4 ? ".m4a" : ".aac")));
            MediaBufferAllocator allocator = new(512 * 1024);
            await using TrackingRandomAccessSource source = new(input);
            await using IIndexedContainerReader reader = mp4 ? await Mp4Reader.OpenAsync(source, allocator) :
                await AdtsIndexedReader.OpenAsync(source, allocator);
            Assert.Equal(16, reader.GetPacketCount(0));
            for (int index = 0; index < packets.Length; index++)
            {
                using MediaPacket packet = await reader.ReadPacketAtAsync(0, index);
                Assert.Equal(packets[index], packet.Data.ToArray());
            }
            EncodedStreamInfo stream = AacSbrDecodeTestInput.Stream(channels, explicitSbr);
            AacDecoderFactory factory = new();
            IRandomAccessInspector inspector = factory.CreateRandomAccessInspector(stream);
            using MediaDecodePipeline<AudioFrame> pipeline = new(reader, factory.Create(stream, allocator), 0, randomAccessInspector: inspector);
            Dictionary<int, byte[]> observed = [];
            double energy = 0, error = 0, maximum = 0;
            foreach (int index in new[] { 13, 15, 0, 12, 11, 14, 13, 15 })
            {
                source.Reads.Clear();
                MediaTimestamp target = new(index * 1024, new(1, 24000));
                await pipeline.SeekAsync(target);
                using AudioFrame frame = (await pipeline.ReadAsync())!;
                Assert.Equal(0, frame.Timestamp!.Value.CompareTo(target));
                Assert.Equal(2048, frame.SamplesPerChannel);
                Assert.Equal(48000, frame.Format.SampleRate);
                Assert.Equal(channels, frame.Format.Channels.Count);
                byte[] actual = frame.GetPlane(0).ToArray();
                if (observed.TryGetValue(index, out byte[]? previous))
                {
                    Assert.Equal(previous, actual);
                }
                observed[index] = actual;
                int bytes = 2048 * channels * sizeof(float);
                byte[] expected = index >= 13 ? restart.AsSpan((index - 11) * bytes, bytes).ToArray() : continuous.AsSpan(index * bytes, bytes).ToArray();
                ReadOnlySpan<float> left = MemoryMarshal.Cast<byte, float>(actual);
                ReadOnlySpan<float> right = MemoryMarshal.Cast<byte, float>(expected);
                for (int sample = 0; sample < left.Length; sample++)
                {
                    Assert.True(float.IsFinite(left[sample]));
                    double difference = left[sample] - right[sample];
                    energy += right[sample] * (double)right[sample];
                    error += difference * difference;
                    maximum = Math.Max(maximum, Math.Abs(difference));
                }
                if (observed.Count == 1)
                {
                    Assert.True(source.Reads.Sum(range => range.Length) < packets.Sum(packet => packet.Length) / 2);
                    long earliest = mp4 ? ((Mp4Reader)reader).GetSampleInfo(0, 11).Offset :
                        packets.Take(11).Sum(packet => packet.Length + 7) + 7;
                    Assert.All(source.Reads, range => Assert.True(range.Offset >= earliest));
                }
                if (index == 15)
                {
                    Assert.Null(await pipeline.ReadAsync());
                }
            }
            double snr = 10 * Math.Log10(energy / error);
            Assert.True(snr > 75 && maximum <= 0.00025, $"Restart PCM SNR {snr:F3} dB; maximum error {maximum:G8}.");
            pipeline.Dispose();
            await reader.DisposeAsync();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public async Task InspectorRequiresActualSbrHeaderAndDoesNotRetainPacketState(int channels)
        {
            byte[][] packets = await AacSbrDecodeTestInput.ReadPacketsAsync(Root, channels);
            IRandomAccessRecoveryInspector inspector = Assert.IsAssignableFrom<IRandomAccessRecoveryInspector>(
                new AacDecoderFactory().CreateRandomAccessInspector(AacSbrDecodeTestInput.Stream(channels, false)));
            MediaBufferAllocator allocator = new(64 * 1024);
            foreach (int index in new[] { 15, 11, 1, 0, 11, 15 })
            {
                using MediaPacket packet = AacSbrDecodeTestInput.Packet(allocator, packets[index], index);
                RandomAccessRecovery? recovery = inspector.InspectRecovery(packet);
                Assert.Equal(index is 0 or 11, recovery.HasValue);
                if (recovery.HasValue)
                {
                    Assert.Equal(2, recovery.Value.RequiredPrerollPackets);
                }
                Assert.Equal(packet.Data.Length, allocator.AllocatedBytes);
            }
            using MediaPacket truncated = AacSbrDecodeTestInput.Packet(allocator, packets[0][..^1], 0);
            Assert.Throws<InvalidDataException>(() => inspector.InspectRecovery(truncated));
            Assert.Throws<OperationCanceledException>(() => inspector.InspectRecovery(truncated, new(true)));
        }

        private static string Root => Path.Combine(AppContext.BaseDirectory, "Data", "AacSbr");
    }
}

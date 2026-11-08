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
    internal static class AacSbrSeekTestInput
    {
        public static async Task VerifyAsync(string root, int channels, bool explicitSbr, bool mp4, bool singleRate = false)
        {
            byte[][] packets = await AacSbrDecodeTestInput.ReadPacketsAsync(root, channels);
            byte[] continuous = await AacSbrDecodeTestInput.ReadPcmAsync(root, channels, singleRate: singleRate);
            byte[] restart = await AacSbrDecodeTestInput.ReadPcmAsync(root, channels, true, singleRate);
            int samplesPerFrame = singleRate ? 1024 : 2048;
            int sampleRate = singleRate ? 24000 : 48000;
            string stem = channels switch { 1 => "transients-mono", 2 => "transients-stereo", _ => throw new ArgumentOutOfRangeException(nameof(channels)) };
            byte[] input = await File.ReadAllBytesAsync(Path.Combine(root, stem + (mp4 ? ".m4a" : ".aac")));
            MediaBufferAllocator allocator = new(512 * 1024);
            await using TrackingRandomAccessSource source = new(input);
            await using IIndexedContainerReader reader = mp4 ? await Mp4Reader.OpenAsync(source, allocator) :
                await AdtsIndexedReader.OpenAsync(source, allocator);
            Require(reader.GetPacketCount(0) == 16);
            for (int index = 0; index < packets.Length; index++)
            {
                using MediaPacket packet = await reader.ReadPacketAtAsync(0, index);
                Require(packet.Data.Span.SequenceEqual(packets[index]));
            }
            EncodedStreamInfo stream = singleRate ? AacSbrDecodeTestInput.SingleRateStream(channels, explicitSbr)
                : AacSbrDecodeTestInput.Stream(channels, explicitSbr);
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
                using AudioFrame frame = await pipeline.ReadAsync() ?? throw new InvalidDataException("HE-AAC seeking lost its requested output frame.");
                Require(frame.Timestamp == target);
                Require(frame.SamplesPerChannel == samplesPerFrame);
                Require(frame.Format.SampleRate == sampleRate);
                Require(frame.Format.Channels.Count == channels);
                byte[] actual = frame.GetPlane(0).ToArray();
                if (observed.TryGetValue(index, out byte[]? previous))
                {
                    Require(actual.AsSpan().SequenceEqual(previous));
                }
                observed[index] = actual;
                int bytes = samplesPerFrame * channels * sizeof(float);
                byte[] expected = index >= 13 ? restart.AsSpan((index - 11) * bytes, bytes).ToArray() : continuous.AsSpan(index * bytes, bytes).ToArray();
                ReadOnlySpan<float> left = MemoryMarshal.Cast<byte, float>(actual);
                ReadOnlySpan<float> right = MemoryMarshal.Cast<byte, float>(expected);
                for (int sample = 0; sample < left.Length; sample++)
                {
                    Require(float.IsFinite(left[sample]));
                    double difference = left[sample] - right[sample];
                    energy += right[sample] * (double)right[sample];
                    error += difference * difference;
                    maximum = Math.Max(maximum, Math.Abs(difference));
                }
                if (observed.Count == 1)
                {
                    Require(source.Reads.Sum(range => range.Length) < packets.Sum(packet => packet.Length) / 2);
                    long earliest = mp4 ? ((Mp4Reader)reader).GetSampleInfo(0, 11).Offset :
                        packets.Take(11).Sum(packet => packet.Length + 7) + 7;
                    Require(source.Reads.All(range => range.Offset >= earliest));
                }
                if (index == 15)
                {
                    Require(await pipeline.ReadAsync() is null);
                }
            }
            double snr = 10 * Math.Log10(energy / error);
            Require(snr > 75 && maximum <= 0.00025);
            pipeline.Dispose();
            await reader.DisposeAsync();
            Require(allocator.AllocatedBytes == 0);
        }

        private static void Require(bool value)
        {
            if (!value)
            {
                throw new InvalidDataException("HE-AAC random-access PCM, clocks, source-read bounds or buffer ownership verification failed.");
            }
        }
    }
}

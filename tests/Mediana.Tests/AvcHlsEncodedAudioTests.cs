// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Aac;
using Mediana.Codecs;
using System.Runtime.InteropServices;
using Mediana.Codecs.H264;
using Mediana.Containers;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Processing;
using Mediana.Streaming.Hls;
using Mediana.Formats.Audio;

namespace Mediana.Tests
{
    public class AvcHlsEncodedAudioTests
    {
        [Fact]
        public async Task ColdEncodedAudioWindowsPreservePacketClocksAndSignalWithoutReadingThePrecedingMedia()
        {
            byte[] bytes = await Mp4TestData.ReadAsync("avc-aac-on-demand.mp4");
            MediaBufferAllocator allocator = new(8 * 1024 * 1024);
            await using TrackingRandomAccessSource source = new(bytes);
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
            {
                MediaTimeRange presentation = ((IContainerPresentationReader)reader).GetPresentationRange(1)!.Value;
                AacConfiguration configuration = AacConfiguration.ReadAudioSpecificConfig(reader.Streams[1].Configuration.Span, out _)!;
                using (AacDecoderWorkspace workspace = new(allocator, new(configuration.ChannelConfiguration)))
                {
                    bool noise = false;
                    for (int index = 0; index < 16; index++)
                    {
                        using MediaPacket packet = await reader.ReadPacketAtAsync(1, index);
                        foreach (AacChannelState channel in workspace.Channels) { channel.ClearFrame(); }
                        uint seed = 0x13579bdf;
                        AacAccessUnitParser.Read(packet.Data.Span, configuration.FrequencyIndex, workspace, ref seed, default);
                        noise |= seed != 0x13579bdf;
                    }
                    Assert.True(noise, "The compressed source must exercise perceptual noise substitution and its nonunique synthesized noise phase.");
                }
                MediaEngine engine = new MediaEngineBuilder().AddAudioDecoder(new AacDecoderFactory()).Build();
                AacEncoderOptions options = new() { BitRate = 64000 };
                ProcessingPacket[] continuous;
                await using (MediaProcessingSession processing = await engine.OpenProcessingAsync(reader,
                    [new AudioEncodingRequest(1, 1, new AacEncoderFactory(options))], allocator))
                {
                    await using ProcessingPacketWriter writer = new();
                    await processing.WriteAsync(writer);
                    continuous = writer.Packets.ToArray();
                }
                long retained = allocator.AllocatedBytes;
                source.Reads.Clear();
                VideoFormat video = new(64, 48, VideoPixelFormat.Yuv420P8,
                    new(ColorPrimaries.Bt709, TransferFunction.Bt709, MatrixCoefficients.Bt709, ColorRange.Limited, ChromaLocation.Left), new(1, 1));
                AudioFormat audio = new(48000, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved, "front-center");
                await using AvcHlsTranscodeSession session = AvcHlsTranscodeSession.Create(reader, 0, new H264DecoderFactory(), video,
                    new MediaDuration(1, new(1, 1)), allocator, preparation: new VideoPreparationOptions { MaximumWidth = 32 },
                    audioEncoding: new(1, new AacDecoderFactory(), audio, presentation, options));
                Assert.Empty(source.Reads);
                using MemoryStream init = new();
                await session.WriteInitializationAsync(init);
                Assert.Empty(source.Reads);
                Assert.Equal(retained, allocator.AllocatedBytes);
                byte[]? tail = null;
                Dictionary<MediaTimestamp, byte[]> observed = [];
                foreach (int number in new[] { 11, 0, 5, 11 })
                {
                    source.Reads.Clear();
                    using MemoryStream output = new();
                    await session.WriteSegmentAsync(number, output);
                    byte[] fragment = output.ToArray();
                    Assert.True(source.Reads.Sum(read => read.Length) < bytes.Length / 2);
                    if (number != 0)
                    {
                        long restart = reader.GetSampleInfo(0, number / 2 * 10).Offset;
                        Assert.All(source.Reads, read => Assert.True(read.Offset >= restart));
                    }
                    VerifyPackets(number, fragment);
                    Assert.Equal(retained, allocator.AllocatedBytes);
                    if (number == 11)
                    {
                        if (tail is not null) { Assert.Equal(tail, fragment); }
                        tail = fragment;
                    }
                }
                long packets = 0;
                List<ProcessingPacket> assembled = [];
                for (int number = 0; number < session.Windows.Count; number++)
                {
                    using MemoryStream output = new();
                    await session.WriteSegmentAsync(number, output);
                    packets += VerifyPackets(number, output.ToArray(), assembled);
                }
                Assert.Equal(continuous.Length, packets);
                float[] expectedSignal = Decode(continuous, session.Streams[1], allocator);
                float[] actualSignal = Decode(assembled, session.Streams[1], allocator);
                Assert.Equal(expectedSignal.Length, actualSignal.Length);
                double energy = 0, error = 0;
                for (int sample = 0; sample < actualSignal.Length; sample++)
                {
                    energy += expectedSignal[sample] * (double)expectedSignal[sample];
                    double difference = actualSignal[sample] - expectedSignal[sample];
                    error += difference * difference;
                }
                double snr = 10 * Math.Log10(energy / error);
                Assert.True(snr > 35, $"Segmented/continuous decoded audio SNR: {snr:F3} dB.");
                Assert.DoesNotContain("#EXT-X-INDEPENDENT-SEGMENTS", session.CreateVodPlaylist("init.mp4", number => $"{number}.m4s"));

                long VerifyPackets(int number, ReadOnlyMemory<byte> fragment, List<ProcessingPacket>? collected = null)
                {
                    ReadOnlyMemory<byte> track = Mp4TestBoxes.GetAll(Mp4TestBoxes.Get(fragment, "moof"), "traf")
                        .Single(item => Mp4TestBoxes.UInt32(Mp4TestBoxes.Get(item, "tfhd"), 4) == 2);
                    ReadOnlyMemory<byte> run = Mp4TestBoxes.Get(track, "trun");
                    long decode = checked((long)Mp4TestBoxes.UInt64(Mp4TestBoxes.Get(track, "tfdt"), 4) - 1024);
                    int offset = checked((int)Mp4TestBoxes.UInt32(run, 8));
                    uint count = Mp4TestBoxes.UInt32(run, 4);
                    AacEncodingInterval plan = AacEncodingInterval.Create(new(session.Windows[number].Start, session.Windows[number].End), presentation, 48000);
                    ProcessingPacket[] expected = continuous.Where(packet => packet.Pts!.Value.CompareTo(plan.Output.Start) >= 0 && packet.Pts.Value.CompareTo(plan.Output.End) < 0).ToArray();
                    Assert.Equal(expected.Length, (int)count);
                    for (int index = 0; index < count; index++)
                    {
                        int duration = checked((int)Mp4TestBoxes.UInt32(run, 12 + 16 * index));
                        int length = checked((int)Mp4TestBoxes.UInt32(run, 16 + 16 * index));
                        Assert.Equal(expected[index].Pts, new MediaTimestamp(decode, new(1, 48000)));
                        byte[] data = fragment.Slice(offset, length).ToArray();
                        MediaTimestamp time = new(decode, new(1, 48000));
                        if (observed.TryGetValue(time, out byte[]? previous)) { Assert.Equal(previous, data); }
                        else { observed.Add(time, data); }
                        collected?.Add(new(1, time, time, new(duration, new(1, 48000)), data));
                        decode += duration;
                        offset += length;
                    }
                    return count;
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static float[] Decode(IEnumerable<ProcessingPacket> packets, EncodedStreamInfo stream, MediaBufferAllocator allocator)
        {
            using IMediaCodec<MediaPacket, AudioFrame> decoder = new AacDecoderFactory().Create(stream, allocator);
            List<float> signal = [];
            foreach (ProcessingPacket encoded in packets)
            {
                using WritableMediaBuffer writable = allocator.Allocate(encoded.Data.Length);
                encoded.Data.CopyTo(writable.Memory.Span);
                using MediaBuffer storage = writable.Publish();
                using MediaPacket packet = new(stream.StreamIndex, storage, encoded.Pts, encoded.Dts, encoded.Duration);
                Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet));
                using AudioFrame frame = decoder.Receive().Value;
                signal.AddRange(MemoryMarshal.Cast<byte, float>(frame.GetPlane(0).Span).ToArray());
            }
            return signal.ToArray();
        }
    }
}

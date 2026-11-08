// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Codecs;
using Mediana.Codecs.Aac;
using Mediana.Codecs.H264;
using Mediana.Containers;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    public class AacSbrHlsTests
    {
        [Fact]
        public async Task ColdOutOfOrderSbrSegmentsUseLocalRestartAndKeepEncodedAudioReproducible()
        {
            byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "AacSbr", "transients-av.mp4"));
            MediaBufferAllocator allocator = new(8 * 1024 * 1024);
            await using TrackingRandomAccessSource source = new(bytes);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, new() { TimestampMode = Mp4TimestampMode.LinearMovie });
            MediaTimeRange presentation = ((IContainerPresentationReader)reader).GetPresentationRange(1)!.Value;
            VideoFormat video = new(64, 48, VideoPixelFormat.Yuv420P8,
                new(ColorPrimaries.Bt709, TransferFunction.Bt709, MatrixCoefficients.Bt709, ColorRange.Limited, ChromaLocation.Left), new(1, 1));
            AudioFormat audio = new(48000, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved, "front-left", "front-right");
            source.Reads.Clear();
            await using AvcHlsTranscodeSession session = AvcHlsTranscodeSession.Create(reader, 0, new H264DecoderFactory(), video,
                new MediaDuration(2, new(1, 5)), allocator, preparation: new() { MaximumWidth = 32 },
                audioEncoding: new(1, new AacDecoderFactory(), audio, presentation, new() { BitRate = 64000 }));
            Assert.Equal(5, session.Windows.Count);
            Assert.Empty(source.Reads);
            long retained = allocator.AllocatedBytes;
            byte[]? tail = null;
            foreach (int number in new[] { 4, 0, 2, 4 })
            {
                source.Reads.Clear();
                using MemoryStream output = new();
                await session.WriteSegmentAsync(number, output);
                byte[] fragment = output.ToArray();
                if (number == 4)
                {
                    Mp4SampleInfo[] permitted = [
                        .. Enumerable.Range(36, 12).Select(index => reader.GetSampleInfo(0, index)),
                        .. Enumerable.Range(33, 13).Select(index => reader.GetSampleInfo(1, index))];
                    Assert.All(source.Reads, range => Assert.Contains(permitted,
                        sample => range.Offset >= sample.Offset && range.Offset + range.Length <= sample.Offset + sample.Size));
                    if (tail is not null) { Assert.Equal(tail, fragment); }
                    tail = fragment;
                }
                ReadOnlyMemory<byte> track = Mp4TestBoxes.GetAll(Mp4TestBoxes.Get(fragment, "moof"), "traf")
                    .Single(item => Mp4TestBoxes.UInt32(Mp4TestBoxes.Get(item, "tfhd"), 4) == 2);
                ReadOnlyMemory<byte> run = Mp4TestBoxes.Get(track, "trun");
                long decode = checked((long)Mp4TestBoxes.UInt64(Mp4TestBoxes.Get(track, "tfdt"), 4) - 1024);
                int offset = checked((int)Mp4TestBoxes.UInt32(run, 8));
                int count = checked((int)Mp4TestBoxes.UInt32(run, 4));
                Assert.True(count > 0);
                AacEncodingInterval plan = AacEncodingInterval.Create(new(session.Windows[number].Start, session.Windows[number].End), presentation, 48000);
                using (IMediaCodec<MediaPacket, AudioFrame> decoder = new AacDecoderFactory().Create(session.Streams[1], allocator))
                {
                    for (int index = 0; index < count; index++)
                    {
                        int duration = checked((int)Mp4TestBoxes.UInt32(run, 12 + 16 * index));
                        int length = checked((int)Mp4TestBoxes.UInt32(run, 16 + 16 * index));
                        Assert.Equal(1024, duration);
                        MediaTimestamp time = new(decode, new(1, 48000));
                        Assert.True(time.CompareTo(plan.Output.Start) >= 0 && time.CompareTo(plan.Output.End) < 0);
                        using WritableMediaBuffer writable = allocator.Allocate(length);
                        fragment.AsSpan(offset, length).CopyTo(writable.Memory.Span);
                        using MediaBuffer buffer = writable.Publish();
                        using MediaPacket packet = new(1, buffer, time);
                        Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(packet));
                        using AudioFrame frame = decoder.Receive().Value;
                        Assert.Equal(48000, frame.Format.SampleRate);
                        Assert.Equal(2, frame.Format.Channels.Count);
                        foreach (float value in MemoryMarshal.Cast<byte, float>(frame.GetPlane(0).Span))
                        {
                            Assert.True(float.IsFinite(value));
                        }
                        offset += length;
                        decode += duration;
                    }
                }
                Assert.Equal(retained, allocator.AllocatedBytes);
            }
            await session.DisposeAsync();
            await reader.DisposeAsync();
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}

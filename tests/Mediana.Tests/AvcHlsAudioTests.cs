// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Processing;
using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    public class AvcHlsAudioTests
    {
        [Fact]
        public async Task ColdAudiovisualRequestsAreRepeatableBoundedAndPreserveTheAudioPacketGrid()
        {
            byte[] input = await Mp4TestData.ReadAsync("avc-aac-on-demand.mp4");
            MediaBufferAllocator allocator = new(8 * 1024 * 1024);
            await using TrackingRandomAccessSource source = new(input);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, new() { TimestampMode = Mp4TimestampMode.LinearMovie });
            source.Reads.Clear();
            long retained = allocator.AllocatedBytes;
            VideoFormat format = new(64, 48, VideoPixelFormat.Yuv420P8,
                new(ColorPrimaries.Bt709, TransferFunction.Bt709, MatrixCoefficients.Bt709, ColorRange.Limited, ChromaLocation.Left), new(1, 1));
            await using AvcHlsTranscodeSession session = AvcHlsTranscodeSession.Create(reader, 0, new H264DecoderFactory(), format,
                new MediaDuration(1, new(1, 1)), allocator, preparation: new VideoPreparationOptions { MaximumWidth = 32 }, copyAudioStreamIndex: 1);
            Assert.Equal(2, session.Streams.Count);
            Assert.Empty(source.Reads);
            using MemoryStream init = new();
            await session.WriteInitializationAsync(init);
            Assert.Empty(source.Reads);
            Assert.Equal(2, Mp4TestBoxes.GetAll(Mp4TestBoxes.Get(init.ToArray(), "moov"), "trak").Count);
            byte[]? previous = null;
            foreach (int number in new[] { 11, 0, 5, 11 })
            {
                source.Reads.Clear();
                using MemoryStream output = new();
                HlsMediaSegment segment = await session.WriteSegmentAsync(number, output);
                byte[] bytes = output.ToArray();
                Assert.Equal(bytes.Length, segment.ByteLength);
                Assert.True(source.Reads.Sum(read => read.Length) < input.Length / 2);
                if (number != 0)
                {
                    long restart = reader.GetSampleInfo(0, number / 2 * 10).Offset;
                    Assert.All(source.Reads, read => Assert.True(read.Offset >= restart));
                }
                VerifyAudio(reader, number, bytes, input);
                Assert.Equal(retained, allocator.AllocatedBytes);
                if (number == 11)
                {
                    if (previous is not null) { Assert.Equal(previous, bytes); }
                    previous = bytes;
                }
            }
            Assert.DoesNotContain("#EXT-X-INDEPENDENT-SEGMENTS", session.CreateVodPlaylist("init.mp4", index => $"{index}.m4s"));
        }

        [Fact]
        public async Task ConsecutiveWindowsCarryEveryOriginalAudioPacketOnceIncludingInitialPreroll()
        {
            MediaBufferAllocator allocator = new(8 * 1024 * 1024);
            byte[] input = await Mp4TestData.ReadAsync("avc-aac-on-demand.mp4");
            await using TrackingRandomAccessSource source = new(input);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, new() { TimestampMode = Mp4TimestampMode.LinearMovie });
            VideoFormat format = new(64, 48, VideoPixelFormat.Yuv420P8,
                new(ColorPrimaries.Bt709, TransferFunction.Bt709, MatrixCoefficients.Bt709, ColorRange.Limited, ChromaLocation.Left), new(1, 1));
            await using AvcHlsTranscodeSession session = AvcHlsTranscodeSession.Create(reader, 0, new H264DecoderFactory(), format,
                new MediaDuration(1, new(1, 1)), allocator, copyAudioStreamIndex: 1);
            long count = 0;
            foreach (HlsVideoWindow window in session.Windows)
            {
                using MemoryStream output = new();
                await session.WriteSegmentAsync(window.Number, output);
                count += VerifyAudio(reader, window.Number, output.ToArray(), input);
            }
            Assert.Equal(reader.GetPacketCount(1), count);
        }

        private static long VerifyAudio(Mp4Reader reader, int number, ReadOnlyMemory<byte> fragment, byte[] input)
        {
            IReadOnlyList<ReadOnlyMemory<byte>> tracks = Mp4TestBoxes.GetAll(Mp4TestBoxes.Get(fragment, "moof"), "traf");
            Assert.Equal(2, tracks.Count);
            ReadOnlyMemory<byte> video = tracks.Single(track => Mp4TestBoxes.UInt32(Mp4TestBoxes.Get(track, "tfhd"), 4) == 1);
            Assert.Equal(5u, Mp4TestBoxes.UInt32(Mp4TestBoxes.Get(video, "trun"), 4));
            ReadOnlyMemory<byte> audio = tracks.Single(track => Mp4TestBoxes.UInt32(Mp4TestBoxes.Get(track, "tfhd"), 4) == 2);
            ReadOnlyMemory<byte> run = Mp4TestBoxes.Get(audio, "trun");
            long decode = (long)Mp4TestBoxes.UInt64(Mp4TestBoxes.Get(audio, "tfdt"), 4) - 1024;
            int offset = (int)Mp4TestBoxes.UInt32(run, 8);
            List<Mp4SampleInfo> expected = Enumerable.Range(0, (int)reader.GetPacketCount(1)).Select(index => reader.GetSampleInfo(1, index))
                .Where(sample => (number == 0 || sample.PresentationTime.CompareTo(new(number, new(1, 1))) >= 0) &&
                    sample.PresentationTime.CompareTo(new(number + 1, new(1, 1))) < 0).ToList();
            Assert.Equal((uint)expected.Count, Mp4TestBoxes.UInt32(run, 4));
            for (int index = 0; index < expected.Count; index++)
            {
                Mp4SampleInfo sample = expected[index];
                int length = (int)Mp4TestBoxes.UInt32(run, 16 + index * 16);
                Assert.Equal(sample.DecodeTime, new(decode, new(1, 48000)));
                Assert.Equal(sample.Size, length);
                Assert.Equal(sample.Duration, new(Mp4TestBoxes.UInt32(run, 12 + index * 16), new(1, 48000)));
                Assert.Equal(input.AsSpan((int)sample.Offset, length).ToArray(), fragment.Slice(offset, length).ToArray());
                decode += Mp4TestBoxes.UInt32(run, 12 + index * 16);
                offset += length;
            }
            Assert.Equal(fragment.Length, offset);
            return expected.Count;
        }
    }
}

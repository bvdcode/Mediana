// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Security.Cryptography;
using Mediana.Codecs;
using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Execution;
using Mediana.IO;

namespace Mediana.Tests
{
    public class H264PresentationPrerollTests
    {
        [Fact]
        public async Task DiscardsCompleteNonReferencePrerollWhileRetainingTheOpeningPicture()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Data", "Mp4", "hls-on-demand.mp4");
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            await using (RandomAccessStreamByteSource source = new(File.OpenRead(path)))
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator,
                new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
            {
                H264DecoderFactory factory = new();
                EncodedStreamInfo stream = reader.Streams[0];
                IMediaCodec<MediaPacket, VideoFrame> decoder = factory.Create(stream, allocator);
                IRandomAccessInspector inspector = ((IRandomAccessDecoderFactory)factory).CreateRandomAccessInspector(stream);
                using MediaDecodePipeline<VideoFrame> pipeline = new(reader, decoder, stream.StreamIndex,
                    randomAccessInspector: inspector);
                MediaTimestamp origin = new(0, new(1, 1));
                MediaTimestamp openingPicture = new(1, new(1, 1));
                MediaTimestamp presentationStart = new(11, new(1, 10));
                await pipeline.SeekAsync(origin);
                List<(MediaTimestamp Timestamp, MediaDuration? Duration, string Pixels)> baseline = await ReadFramesAsync(pipeline);

                await pipeline.SeekAsync(origin);
                Assert.IsAssignableFrom<IVideoPresentationPrerollDecoder>(decoder).SetPresentationStart(presentationStart);
                List<(MediaTimestamp Timestamp, MediaDuration? Duration, string Pixels)> trimmed = await ReadFramesAsync(pipeline);
                Assert.True(trimmed.Count < baseline.Count);
                Assert.Contains(trimmed, frame => frame.Timestamp == openingPicture);
                Assert.Equal(baseline.Where(frame => frame.Timestamp.CompareTo(openingPicture) >= 0),
                    trimmed.Where(frame => frame.Timestamp.CompareTo(openingPicture) >= 0));

                await pipeline.SeekAsync(origin);
                Assert.Equal(baseline, await ReadFramesAsync(pipeline));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static async Task<List<(MediaTimestamp Timestamp, MediaDuration? Duration, string Pixels)>> ReadFramesAsync(
            MediaDecodePipeline<VideoFrame> pipeline)
        {
            List<(MediaTimestamp Timestamp, MediaDuration? Duration, string Pixels)> frames = [];
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            while (await pipeline.ReadAsync() is VideoFrame frame)
            {
                using (frame)
                {
                    for (int plane = 0; plane < frame.Planes.Count; plane++)
                    {
                        for (int row = 0; row < frame.Planes[plane].Height; row++)
                        {
                            hash.AppendData(frame.GetRow(plane, row));
                        }
                    }
                    frames.Add((frame.Timestamp!.Value, frame.Duration, Convert.ToHexString(hash.GetHashAndReset())));
                }
            }
            return frames;
        }
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Containers.Matroska;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Execution;

namespace Mediana.Tests
{
    public class IndexedVideoSegmentPlanTests
    {
        [Theory]
        [InlineData("FrameRate", "frame-rate-cfr.mp4")]
        [InlineData("FrameRate", "frame-rate-vfr.mp4")]
        [InlineData("FrameRate", "frame-rate-edited.mp4")]
        [InlineData("Matroska", "avc-aac-attachment.mkv")]
        public async Task PlansRealReorderedAndVariableRateTracksWithoutReadingPayload(string group, string file)
        {
            byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", group, file));
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            await using TrackingRandomAccessSource source = new(bytes);
            await using (IIndexedPacketTimingReader reader = group switch
            {
                "FrameRate" => await Mp4Reader.OpenAsync(source, allocator, new() { TimestampMode = Mp4TimestampMode.LinearMovie }),
                "Matroska" => await MatroskaReader.OpenAsync(source, allocator),
                _ => throw new ArgumentOutOfRangeException(nameof(group))
            })
            {
                source.Reads.Clear();
                EncodedStreamInfo video = reader.Streams.First(stream => stream.Kind == MediaKind.Video);
                using VideoSegmentPlan plan = VideoSegmentPlanner.Create(reader, video.StreamIndex, new(1, new(1, 10)), allocator);
                Assert.NotEmpty(plan);
                for (int index = 1; index < plan.Count; index++) { Assert.Equal(plan[index - 1].End, plan[index].Start); }
                Assert.Empty(source.Reads);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task FacadeReturnsAnOwnedPlanWithoutReopeningOrTransferringTheContainer()
        {
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync("hls-on-demand.mp4"));
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory(new() { TimestampMode = Mp4TimestampMode.LinearMovie })).Build();
            MediaBufferAllocator workspace = new(65536);
            await using ContainerSession container = await engine.OpenContainerAsync(source);
            source.Reads.Clear();
            using VideoSegmentPlan plan = container.PlanVideoSegments(0, new(1, new(1, 1)), workspace);
            Assert.Equal(12, plan.Count);
            Assert.Empty(source.Reads);
            await container.DisposeAsync();
            Assert.Equal(new MediaTimestamp(11, new(1, 1)), plan[11].Start);
            plan.Dispose();
            Assert.Equal(0, workspace.AllocatedBytes);
        }
    }
}

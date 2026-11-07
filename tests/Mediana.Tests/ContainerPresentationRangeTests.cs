// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Tests
{
    public class ContainerPresentationRangeTests
    {
        [Theory]
        [InlineData(Mp4TimestampMode.Media, false)]
        [InlineData(Mp4TimestampMode.Media, true)]
        [InlineData(Mp4TimestampMode.LinearMovie, false)]
        [InlineData(Mp4TimestampMode.LinearMovie, true)]
        public async Task ExplicitSelectionUsesPacketTimelineWithoutReadingPayload(Mp4TimestampMode mode, bool wide)
        {
            byte[] bytes = Mp4EditTestData.Replace(await Mp4TestData.ReadAsync("avc-tail.mov"), wide,
                (1, -1, 65536), (123, 7, 65536));
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(bytes);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, new() { TimestampMode = mode });
            int before = source.Reads.Count;
            IContainerPresentationReader presentation = reader;
            MediaTimeRange range = presentation.GetPresentationRange(0)!.Value;
            MediaTimestamp expected = mode switch
            {
                Mp4TimestampMode.Media => new(7, new(1, 10240)),
                Mp4TimestampMode.LinearMovie => new(1, new(1, 1000)),
                _ => throw new ArgumentOutOfRangeException(nameof(mode))
            };
            Assert.Equal(expected, range.Start);
            Assert.Equal(new MediaTimestamp(123, new(1, 1000)), new(range.Duration.Value, range.Duration.TimeBase));
            Assert.Equal(before, source.Reads.Count);
            using MediaPacket first = (await reader.ReadPacketAsync())!;
            Assert.Equal(reader.GetSampleInfo(0, 0).PresentationTime, first.PresentationTime);
        }

        [Fact]
        public async Task AbsentAndEmptySelectionsRemainDistinctAndNonlinearEditsAreNotInvented()
        {
            foreach ((long Duration, long MediaTime, int Rate)[] edits in new (long, long, int)[][]
                { [], [(0, 7, 65536)], [(1, 0, 32768)], [(1, 0, 65536), (1, 7, 65536)] })
            {
                byte[] bytes = Mp4EditTestData.Replace(await Mp4TestData.ReadAsync("avc-tail.mov"), false, edits);
                MediaBufferAllocator allocator = new(1024 * 1024);
                await using TrackingRandomAccessSource source = new(bytes);
                Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
                await using (reader)
                {
                    if (edits.Length == 0) { Assert.Null(reader.GetPresentationRange(0)); }
                    else if (edits[0].Duration == 0) { Assert.Equal(0, reader.GetPresentationRange(0)!.Value.Duration.Value); }
                    else { Assert.Throws<NotSupportedException>(() => reader.GetPresentationRange(0)); }
                    Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetPresentationRange(7));
                    using CancellationTokenSource cancellation = new();
                    await cancellation.CancelAsync();
                    Assert.Throws<OperationCanceledException>(() => reader.GetPresentationRange(0, cancellation.Token));
                }
                Assert.Throws<ObjectDisposedException>(() => reader.GetPresentationRange(0));
                Assert.Equal(0, allocator.AllocatedBytes);
            }
        }
    }
}

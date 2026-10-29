// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Mp4;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;
using Mediana.Streaming.Hls;

namespace Mediana.Sample
{
    internal static class HlsPlanSample
    {
        private const long BufferBudgetBytes = 64 * 1024 * 1024;

        public static async Task<int> RunAsync(string path, int streamIndex, long targetMilliseconds)
        {
            MediaBufferAllocator allocator = new(BufferBudgetBytes);
            Mp4AvcSegmentPlan plan;
            await using (RandomAccessStreamByteSource source = new(File.OpenRead(path)))
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator,
                new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
            {
                plan = await Mp4AvcSegmentPlan.CreateAsync(reader, streamIndex,
                    new() { TargetDuration = new(targetMilliseconds, new TimeBase(1, 1000)) });
            }
            foreach (Mp4AvcSegment segment in plan.Segments)
            {
                await Console.Out.WriteLineAsync($"Video segment {segment.Number}: first sample {segment.FirstSample}; {segment.SampleCount} samples; PTS {segment.PresentationStart.Value}; duration {segment.Duration.Value}; DTS [{segment.DecodeStart.Value}, {segment.DecodeEnd.Value}); time base {plan.VideoStream.TimeBase.Numerator}/{plan.VideoStream.TimeBase.Denominator}.");
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("Video planning buffers were not released.");
            }
            await Console.Out.WriteLineAsync($"AVC video plan: {plan.Segments.Count} windows. Live buffer bytes: 0.");
            return 0;
        }
    }
}

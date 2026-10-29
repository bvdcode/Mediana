using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;
using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    internal static class HlsPlanTestInput
    {
        public static async Task VerifyAsync(string root)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            Mp4AvcSegmentPlan plan;
            using FileStream input = File.OpenRead(Path.Combine(root, "avc-segments.mp4"));
            await using RandomAccessStreamByteSource source = new(input, leaveOpen: true);
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator,
                new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
            {
                using MediaPacket first = (await reader.ReadPacketAsync())!;
                HlsPlanningOptions options = new() { TargetDuration = new(9, new TimeBase(1, 4)) };
                plan = await Mp4AvcSegmentPlan.CreateAsync(reader, 0, options);
                Mp4AvcSegmentPlan repeated = await Mp4AvcSegmentPlan.CreateAsync(reader, 0, options);
                if (!plan.Segments.SequenceEqual(repeated.Segments))
                {
                    throw new InvalidDataException("Repeated AVC planning changed the immutable video windows.");
                }
                if (plan.Segments.Count != 3 || plan.VideoStream != reader.Streams[0])
                {
                    throw new InvalidDataException("The 7.2-second AVC input must produce three video segment windows.");
                }
                H264RandomAccessInspector inspector = new(plan.VideoStream);
                for (int number = 2; number >= 0; number--)
                {
                    Mp4AvcSegment segment = plan.Segments[number];
                    int expectedCount = number == 2 ? 6 : 15;
                    long expectedDuration = number == 2 ? 12288 : 30720;
                    long decodeStart = number * 30720L - 4096;
                    if (segment.Number != number || segment.FirstSample != number * 15 || segment.SampleCount != expectedCount ||
                        segment.PresentationStart != new MediaTimestamp(number * 30720L, new(1, 10240)) ||
                        segment.Duration != new MediaDuration(expectedDuration, new(1, 10240)) ||
                        segment.DecodeStart != new MediaTimestamp(decodeStart, new(1, 10240)) ||
                        segment.DecodeEnd != new MediaTimestamp(decodeStart + expectedCount * 2048, new(1, 10240)))
                    {
                        throw new InvalidDataException("The AVC segment boundaries differ from independent GOP and timestamp references.");
                    }
                    using MediaPacket packet = await reader.ReadPacketAtAsync(0, segment.FirstSample);
                    if (!inspector.IsIdr(packet))
                    {
                        throw new InvalidDataException("A planned video segment does not begin with an inspected IDR header.");
                    }
                }
                using MediaPacket next = (await reader.ReadPacketAsync())!;
                if (next.StreamIndex != 0 || next.DecodeTime != new MediaTimestamp(-2048, new(1, 10240)))
                {
                    throw new InvalidDataException("Segment planning or reverse boundary reads moved the sequential cursor.");
                }
            }
            if (allocator.AllocatedBytes != 0 || !input.CanRead || plan.Segments[2].FirstSample != 30)
            {
                throw new InvalidOperationException("An immutable segment plan retained reader buffers or source ownership.");
            }
        }
    }
}

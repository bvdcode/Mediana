using Mediana.Containers.Mp4;
using Mediana.Containers.MpegTs;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;
using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    internal static class MpegTsSegmentTestInput
    {
        public static async Task<byte[][]> VerifyAsync(string root)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using MemoryStream first = new();
            using MemoryStream second = new();
            using MemoryStream third = new();
            MemoryStream[] outputs = [first, second, third];
            List<(int Stream, byte[] Data, long Pts, long Dts)> expected = [];
            List<(int Stream, byte[] Data, long Pts, long Dts)>[] windows = [[], [], []];
            IReadOnlyList<EncodedStreamInfo> streams;
            await using (RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, "avc-segments.mp4"))))
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator,
                new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
            {
                Mp4AvcSegmentPlan plan = await Mp4AvcSegmentPlan.CreateAsync(reader, 0,
                    new() { TargetDuration = new(9, new TimeBase(1, 4)) });
                streams = reader.Streams;
                int window = 0;
                int videoSample = 0;
                await using MpegTsWriter writer = MpegTsWriter.Create(first, streams, allocator);
                while (await reader.ReadPacketAsync() is MediaPacket packet)
                {
                    using (packet)
                    {
                        if (packet.StreamIndex == 0)
                        {
                            if (window + 1 < plan.Segments.Count && videoSample == plan.Segments[window + 1].FirstSample)
                            {
                                await writer.BeginSegmentAsync(outputs[++window]);
                            }
                            videoSample++;
                        }
                        (int Stream, byte[] Data, long Pts, long Dts) item =
                            (packet.StreamIndex, packet.Data.ToArray(), Ticks(packet.PresentationTime), Ticks(packet.DecodeTime));
                        expected.Add(item);
                        windows[window].Add(item);
                        await writer.WritePacketAsync(packet);
                    }
                }
                await writer.CompleteAsync();
            }
            if (allocator.AllocatedBytes != 0 || outputs.Any(output => !output.CanWrite))
            {
                throw new InvalidOperationException("Segment output retained media buffers or closed a borrowed stream.");
            }
            byte[][] result = outputs.Select(output => output.ToArray()).ToArray();
            for (int number = 0; number < result.Length; number++)
            {
                ReadOnlySpan<byte> bytes = result[number];
                if (Pid(bytes) != 0 || Pid(bytes[188..]) != 0x1000 || windows[number][0].Stream != 0)
                {
                    throw new InvalidDataException("Each video segment must start with PAT, PMT and an AVC access unit.");
                }
                int[] audioCounts = [123, 141, 75];
                if (windows[number].Count(item => item.Stream == 1) != audioCounts[number])
                {
                    throw new InvalidDataException("Segment AAC coverage differs from the independent decode-order reference.");
                }
                MpegTsTestInput.Verify(bytes, streams, windows[number]);
            }
            MpegTsTestInput.Verify(result.SelectMany(bytes => bytes).ToArray(), streams, expected);
            return result;
        }

        private static int Pid(ReadOnlySpan<byte> packet) => (packet[1] & 31) << 8 | packet[2];

        private static long Ticks(MediaTimestamp? value) => checked(value!.Value.Rescale(new(1, 90000),
            TimestampRounding.NearestEven).Value + 90000);
    }
}

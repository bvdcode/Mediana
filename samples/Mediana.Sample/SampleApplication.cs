using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Execution;
using Mediana.Processing;

namespace Mediana.Sample
{
    internal static class SampleApplication
    {
        public static async Task<int> RunAsync(string[] args)
        {
            if (args.Length == 2 && args[0] == "--probe-mp4")
            {
                return await Mp4ProbeSample.RunAsync(args[1]);
            }
            if (args.Length > 1 || (args.Length == 1 && args[0] != "--self-test"))
            {
                await Console.Error.WriteLineAsync("Usage: Mediana.Sample [--self-test | --probe-mp4 <path>]");
                return 2;
            }

            MediaEngine engine = new MediaEngineBuilder().Build();
            MediaBufferAllocator allocator = new(1024);
            await using GeneratedPacketSource source = new(allocator);
            using (VideoDecodePipeline pipeline = new(source, new RgbaSampleDecoder(allocator), 0, 8))
            {
                using VideoFrame frame = await pipeline.ReadAsync()
                    ?? throw new InvalidOperationException("The generated frame is missing.");
                using VideoFrame resized = NearestNeighborResize.Apply(frame, 1, 1, allocator);
                if (!resized.Data.Span.SequenceEqual(new byte[] { 255, 0, 0, 255 }))
                {
                    throw new InvalidOperationException("Unexpected resize output.");
                }
                if (await pipeline.ReadAsync() is not null)
                {
                    throw new InvalidOperationException("Unexpected extra output.");
                }
                MediaTimestamp timestamp = new(90_000, new TimeBase(1, 90_000));
                if (timestamp.Rescale(TimeBase.TimeSpanTicks, TimestampRounding.NearestEven).Value != TimeSpan.TicksPerSecond)
                {
                    throw new InvalidOperationException("Unexpected timestamp conversion.");
                }
                await Console.Out.WriteLineAsync("Generated RGBA frame: 2x2 -> 1x1; pixel: FF0000FF; timestamp: 1 second.");
            }

            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("Media buffers were not released.");
            }
            await Console.Out.WriteLineAsync(
                $"Foundation checks passed. Registered production containers: {engine.ContainerImplementations.Count}. Live buffer bytes: {allocator.AllocatedBytes}.");
            return 0;
        }
    }
}

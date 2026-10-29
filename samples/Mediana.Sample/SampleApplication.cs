using System.Globalization;
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
            if (args.Length == 2 && args[0] == "--probe-flac")
            {
                return await FlacProbeSample.RunAsync(args[1]);
            }
            if (args.Length == 4 && args[0] == "--cover-flac" &&
                int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out int pictureIndex))
            {
                return await FlacPictureSample.RunAsync(args[1], args[2], pictureIndex);
            }
            if (args.Length == 7 && args[0] == "--waveform-audio" &&
                int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out int waveformStreamIndex) &&
                int.TryParse(args[4], NumberStyles.None, CultureInfo.InvariantCulture, out int waveformRate) && waveformRate > 0 &&
                int.TryParse(args[5], NumberStyles.None, CultureInfo.InvariantCulture, out int samplesPerWindow) && samplesPerWindow > 0)
            {
                double[] gains = args[6].Split(',').Select(value => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
                return await AudioWaveformSample.RunAsync(args[1], args[2], waveformStreamIndex, waveformRate, samplesPerWindow, gains);
            }
            if (args.Length == 4 && args[0] == "--cover-mp4" &&
                int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out int coverIndex))
            {
                return await Mp4CoverArtSample.RunAsync(args[1], args[2], coverIndex);
            }
            if (args.Length == 6 && args[0] == "--resample-audio" &&
                int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out int resampleStreamIndex) &&
                int.TryParse(args[4], NumberStyles.None, CultureInfo.InvariantCulture, out int sampleRate) && sampleRate > 0)
            {
                double[] gains = args[5].Split(',').Select(value => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
                return await AudioResampleSample.RunAsync(args[1], args[2], resampleStreamIndex, sampleRate, gains);
            }
            if (args.Length == 4 && args[0] == "--decode-audio" &&
                int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out int audioStreamIndex))
            {
                return await AudioDecodeSample.RunAsync(args[1], args[2], audioStreamIndex);
            }
            if (args.Length == 7 && args[0] == "--preview" &&
                int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out int streamIndex) &&
                long.TryParse(args[4], NumberStyles.None, CultureInfo.InvariantCulture, out long milliseconds) &&
                int.TryParse(args[5], NumberStyles.None, CultureInfo.InvariantCulture, out int maximumWidth) && maximumWidth > 0 &&
                int.TryParse(args[6], NumberStyles.None, CultureInfo.InvariantCulture, out int maximumHeight) && maximumHeight > 0)
            {
                return await VideoPreviewSample.RunAsync(args[1], args[2], streamIndex, milliseconds, maximumWidth, maximumHeight);
            }
            if (args.Length == 2 && args[0] == "--probe-mp4")
            {
                return await Mp4ProbeSample.RunAsync(args[1]);
            }
            if (args.Length == 3 && args[0] == "--remux-mp4")
            {
                return await Mp4RemuxSample.RunAsync(args[1], args[2]);
            }
            if (args.Length == 4 && args[0] == "--plan-hls-mp4" &&
                int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out int videoIndex) &&
                long.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out long targetMilliseconds) && targetMilliseconds > 0)
            {
                return await HlsPlanSample.RunAsync(args[1], videoIndex, targetMilliseconds);
            }
            if (args.Length == 6 && args[0] == "--hls-mp4" &&
                int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out int hlsVideoIndex) &&
                TryAudioIndex(args[4], out int? hlsAudioIndex) &&
                long.TryParse(args[5], NumberStyles.None, CultureInfo.InvariantCulture, out long hlsTargetMilliseconds) && hlsTargetMilliseconds > 0)
            {
                return await HlsCopySample.RunAsync(args[1], args[2], hlsVideoIndex, hlsAudioIndex, hlsTargetMilliseconds);
            }
            if (args.Length > 1 || (args.Length == 1 && args[0] != "--self-test"))
            {
                await Console.Error.WriteLineAsync("Usage: Mediana.Sample [--self-test | --probe-flac <input> | --cover-flac <input> <new-image-output> <picture-index> | --probe-mp4 <path> | --cover-mp4 <input> <new-image-output> <cover-index> | --decode-audio <input> <new-output.pcm> <stream-index> | --resample-audio <input> <new-output.pcm> <stream-index> <sample-rate> <comma-separated-channel-gains> | --waveform-audio <input> <new-output.json> <stream-index> <sample-rate> <samples-per-window> <comma-separated-channel-gains> | --preview <input> <new-output.png> <stream-index> <milliseconds> <max-width> <max-height> | --remux-mp4 <input> <new-output.ts> | --plan-hls-mp4 <input> <stream-index> <target-milliseconds> | --hls-mp4 <input> <new-directory> <video-index> <audio-index|none> <target-milliseconds>]");
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

        private static bool TryAudioIndex(string text, out int? index)
        {
            if (text == "none")
            {
                index = null;
                return true;
            }
            if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
            {
                index = value;
                return true;
            }
            index = null;
            return false;
        }
    }
}

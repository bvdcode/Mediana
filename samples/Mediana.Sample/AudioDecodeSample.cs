using Mediana.Core.Audio;
using Mediana.IO;

namespace Mediana.Sample
{
    internal static class AudioDecodeSample
    {
        public static async Task<int> RunAsync(string inputPath, string outputPath, int streamIndex)
        {
            MediaEngine engine = SampleAudioEngine.Create();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(inputPath));
            await using AudioSession session = await engine.OpenAudioAsync(source, streamIndex);
            long samples = 0;
            AudioFormat? format = null;
            await SampleOutputFile.WriteAsync(outputPath, async output =>
            {
                while (true)
                {
                    using AudioFrame? frame = await session.ReadFrameAsync();
                    if (frame is null)
                    {
                        break;
                    }
                    if (format is not null && (format.SampleRate != frame.Format.SampleRate
                        || format.SampleFormat != frame.Format.SampleFormat || !format.Channels.SequenceEqual(frame.Format.Channels)
                        || format.Layout != frame.Format.Layout))
                    {
                        throw new NotSupportedException("The raw PCM output requires one stable audio format.");
                    }
                    format = frame.Format;
                    if (format.Layout != AudioStorageLayout.Interleaved)
                    {
                        throw new NotSupportedException("The raw PCM output requires interleaved samples.");
                    }
                    await output.WriteAsync(frame.GetPlane(0));
                    samples = checked(samples + frame.SamplesPerChannel);
                }
            });
            await session.DisposeAsync();
            if (session.AllocatedBufferBytes != 0)
            {
                throw new InvalidOperationException("Audio buffers were not released.");
            }
            if (format is null)
            {
                await Console.Out.WriteLineAsync("PCM output: no samples. Live buffer bytes: 0.");
                return 0;
            }
            await Console.Out.WriteLineAsync($"PCM output: {samples} samples per channel; {format.SampleRate} Hz; {format.SampleFormat}; "
                + $"{string.Join(", ", format.Channels)}; native-endian interleaved. Live buffer bytes: 0.");
            return 0;
        }
    }
}

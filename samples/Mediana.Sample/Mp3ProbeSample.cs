// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Mp3;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Sample
{
    internal static class Mp3ProbeSample
    {
        private const long BufferBudgetBytes = 64 * 1024 * 1024;

        public static async Task<int> RunAsync(string path)
        {
            MediaBufferAllocator allocator = new(BufferBudgetBytes);
            await using StreamByteSource source = new(File.OpenRead(path));
            await using Mp3Reader reader = await Mp3Reader.OpenAsync(source, allocator);
            Mp3FrameHeader header = reader.FirstFrame;
            await Console.Out.WriteLineAsync($"MP3 {header.Version}: {header.SampleRate} Hz; {header.ChannelCount} channels; {header.SamplesPerFrame} coded samples/frame; first audio byte {reader.AudioOffset}.");
            if (reader.VbrInfo is Mp3VbrInfo info)
            {
                await Console.Out.WriteLineAsync($"{info.Kind}: declared audio frames {info.FrameCount}; bytes {info.ByteCount}; quality {info.Quality}.");
            }
            foreach (Id3TextFrame text in reader.TextFrames)
            {
                await Console.Out.WriteLineAsync($"{text.FrameId} {text.Description}: {string.Join("; ", text.Values)}");
            }
            for (int index = 0; index < reader.Pictures.Count; index++)
            {
                Id3PictureInfo picture = reader.Pictures[index];
                await Console.Out.WriteLineAsync($"Picture {index}: type {picture.PictureType}; {picture.MediaType}; {picture.ByteLength} original bytes; {picture.Description}.");
            }
            await reader.DisposeAsync();
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("MP3 probing retained media buffers.");
            }
            await Console.Out.WriteLineAsync("Leading metadata and first frame only. PCM, CRC, final frame count and trailing tags are not verified. Live buffer bytes: 0.");
            return 0;
        }
    }
}

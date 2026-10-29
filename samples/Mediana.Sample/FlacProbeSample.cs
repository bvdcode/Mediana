// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Flac;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Sample
{
    internal static class FlacProbeSample
    {
        private const long BufferBudgetBytes = 64 * 1024 * 1024;

        public static async Task<int> RunAsync(string path)
        {
            MediaBufferAllocator allocator = new(BufferBudgetBytes);
            await using StreamByteSource source = new(File.OpenRead(path));
            await using FlacReader reader = await FlacReader.OpenAsync(source, allocator);
            FlacConfiguration configuration = reader.Configuration;
            await Console.Out.WriteLineAsync($"Native FLAC: {configuration.SampleRate} Hz; {configuration.ChannelCount} channels; {configuration.BitsPerSample} bits; declared samples {configuration.TotalSamples} (0 means unknown).");
            await Console.Out.WriteLineAsync($"Block size {configuration.MinimumBlockSize}..{configuration.MaximumBlockSize}; first audio byte {reader.AudioOffset}; vendor {reader.Vendor}.");
            foreach (FlacComment comment in reader.Comments)
            {
                await Console.Out.WriteLineAsync($"Comment {comment.Name}={comment.Value}");
            }
            for (int index = 0; index < reader.Pictures.Count; index++)
            {
                FlacPictureInfo picture = reader.Pictures[index];
                await Console.Out.WriteLineAsync($"Picture {index}: type {picture.PictureType}; {picture.MediaType}; {picture.ByteLength} original bytes; declared {picture.Width}x{picture.Height}; {picture.Description}.");
            }
            await reader.DisposeAsync();
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("FLAC probing retained metadata buffers.");
            }
            await Console.Out.WriteLineAsync("Metadata-only probe. Audio frames, CRCs and decoded-audio MD5 are not verified. Live buffer bytes: 0.");
            return 0;
        }
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Mp3;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Sample
{
    internal static class Mp3PictureSample
    {
        private const long BufferBudgetBytes = 64 * 1024 * 1024;

        public static async Task<int> RunAsync(string inputPath, string outputPath, int pictureIndex)
        {
            MediaBufferAllocator allocator = new(BufferBudgetBytes);
            await using StreamByteSource source = new(File.OpenRead(inputPath));
            await using Mp3Reader reader = await Mp3Reader.OpenAsync(source, allocator);
            ArgumentOutOfRangeException.ThrowIfNegative(pictureIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pictureIndex, reader.Pictures.Count);
            Id3PictureInfo info = reader.Pictures[pictureIndex];
            if (info.MediaType == "-->")
            {
                throw new NotSupportedException("The selected MP3 picture contains a URL rather than embedded image bytes.");
            }
            using (MediaBuffer picture = reader.ReadPicture(pictureIndex))
            {
                await reader.DisposeAsync();
                await SampleOutputFile.WriteAsync(outputPath, output => output.WriteAsync(picture.Memory).AsTask());
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("MP3 picture extraction retained media buffers.");
            }
            await Console.Out.WriteLineAsync($"Picture {pictureIndex}: declared {info.MediaType}; {info.ByteLength} original bytes. Live buffer bytes: 0.");
            return 0;
        }
    }
}

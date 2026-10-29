// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Flac;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Cli
{
    internal static class FlacPictureCommand
    {
        private const long BufferBudgetBytes = 64 * 1024 * 1024;

        public static async Task<int> RunAsync(string inputPath, string outputPath, int pictureIndex)
        {
            MediaBufferAllocator allocator = new(BufferBudgetBytes);
            await using StreamByteSource source = new(File.OpenRead(inputPath));
            await using FlacReader reader = await FlacReader.OpenAsync(source, allocator);
            ArgumentOutOfRangeException.ThrowIfNegative(pictureIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pictureIndex, reader.Pictures.Count);
            FlacPictureInfo info = reader.Pictures[pictureIndex];
            if (info.MediaType == "-->")
            {
                throw new NotSupportedException("The selected FLAC picture contains a URI rather than embedded image bytes.");
            }
            using (MediaBuffer picture = reader.ReadPicture(pictureIndex))
            {
                await reader.DisposeAsync();
                await CommandOutputFile.WriteAsync(outputPath, output => output.WriteAsync(picture.Memory).AsTask());
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("FLAC picture extraction retained metadata buffers.");
            }
            await Console.Out.WriteLineAsync($"Picture {pictureIndex}: declared {info.MediaType}; {info.ByteLength} original bytes. Live buffer bytes: 0.");
            return 0;
        }
    }
}

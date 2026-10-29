// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Mp4;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Cli
{
    internal static class Mp4CoverArtCommand
    {
        private const int BufferBudgetBytes = 64 * 1024 * 1024;

        public static async Task<int> RunAsync(string inputPath, string outputPath, int coverIndex)
        {
            MediaBufferAllocator allocator = new(BufferBudgetBytes);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(inputPath));
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            ArgumentOutOfRangeException.ThrowIfNegative(coverIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(coverIndex, reader.CoverArt.Count);
            Mp4CoverArtInfo info = reader.CoverArt[coverIndex];
            string contentType = info.ContentType ?? throw new NotSupportedException("The cover's declared image type is unsupported.");
            using (MediaBuffer cover = reader.ReadCoverArt(coverIndex))
            {
                await reader.DisposeAsync();
                await CommandOutputFile.WriteAsync(outputPath, output => output.WriteAsync(cover.Memory).AsTask());
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("Cover extraction retained MP4 metadata buffers.");
            }
            await Console.Out.WriteLineAsync($"Cover {coverIndex}: {contentType}; {info.ByteLength} original bytes. Live buffer bytes: 0.");
            return 0;
        }
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.IO.Compression;
using static Mediana.Tests.PngDecodeTestData;

namespace Mediana.Tests
{
    [Collection("CommandLine")]
    public class CliImageResourcesTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("mediana-large-image-").FullName;

        [Fact]
        public async Task HeicConversionCompletesAboveFormerPixelBudget()
        {
            string source = Path.Combine(AppContext.BaseDirectory, "Data", "Heif", "large-grid.heic");
            string destination = Path.Combine(directory, "grid.png");

            (int code, _, string error) = await CliTests.RunAsync(["-i", source, destination]);

            Assert.True(code == 0, error);
            PngTestInput.VerifyPreview(await System.IO.File.ReadAllBytesAsync(destination), 4608, 4096,
                [129, 129, 129, 255], 1, (_, _) => (0, 0), tolerance: 1);
            Assert.Single(Directory.GetFiles(directory));
        }

        [Fact]
        public async Task ConversionCompletesAboveFormerPixelAndMemoryBudgets()
        {
            const int width = 8192;
            const int height = 8193;
            byte[] input = File(Chunk("IHDR", Header(width, height, depth: 1, color: 0)),
                Chunk("sRGB", [0]), Chunk("IDAT", Compressed(new byte[(width / 8 + 1) * height])), Chunk("IEND", []));
            string source = Path.Combine(directory, "input.bin");
            string destination = Path.Combine(directory, "output.png");
            await System.IO.File.WriteAllBytesAsync(source, input);

            (int code, _, string error) = await CliTests.RunAsync(["-i", source, destination]);

            Assert.True(code == 0, error);
            Dictionary<string, byte[]> chunks = PngTestInput.ReadChunks(await System.IO.File.ReadAllBytesAsync(destination));
            Assert.Equal(width, BinaryPrimitives.ReadInt32BigEndian(chunks["IHDR"]));
            Assert.Equal(height, BinaryPrimitives.ReadInt32BigEndian(chunks["IHDR"].AsSpan(4)));
            Assert.Equal(new byte[] { 8, 6, 0, 0, 0 }, chunks["IHDR"].AsSpan(8).ToArray());
            byte[] expected = new byte[width * 4 + 1];
            for (int alpha = 4; alpha < expected.Length; alpha += 4)
            {
                expected[alpha] = byte.MaxValue;
            }
            byte[] row = new byte[expected.Length];
            using MemoryStream compressed = new(chunks["IDAT"], writable: false);
            await using ZLibStream pixels = new(compressed, CompressionMode.Decompress);
            for (int y = 0; y < height; y++)
            {
                await pixels.ReadExactlyAsync(row);
                Assert.True(row.AsSpan().SequenceEqual(expected), $"Unexpected pixels on row {y}.");
            }
            Assert.Equal(0, await pixels.ReadAsync(row.AsMemory(0, 1)));
            Assert.Equal(input, await System.IO.File.ReadAllBytesAsync(source));
            Assert.Equal(2, Directory.GetFiles(directory).Length);
        }

        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}

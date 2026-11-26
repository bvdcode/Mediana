// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using Mediana.Core.Video;
using Mediana.Images.Apk;

namespace Mediana.Tests
{
    public class ApkIconReaderTests
    {
        [Theory]
        [InlineData(48, 255, 0, 0)]
        [InlineData(96, 0, 0, 255)]
        [InlineData(192, 0, 255, 0)]
        public async Task ReadsDeclaredRasterAtRequestedDensity(int size, int r, int g, int b)
        {
            using FileStream apk = Open("raster");
            using VideoFrame? frame = await ApkIconReader.ReadAsync(apk, size);
            Assert.NotNull(frame);
            Assert.Equal((size, size, VideoPixelFormat.Rgba32), (frame.Format.Width, frame.Format.Height, frame.Format.PixelFormat));
            byte[] center = Pixel(frame, size / 2, size / 2);
            Assert.InRange((int)center[0], Math.Max(0, r - 3), Math.Min(255, r + 3));
            Assert.InRange((int)center[1], Math.Max(0, g - 3), Math.Min(255, g + 3));
            Assert.InRange((int)center[2], Math.Max(0, b - 3), Math.Min(255, b + 3));
            Assert.Equal(255, center[3]);
            Assert.Equal(0, Pixel(frame, size / 2, 0)[3]);
            Assert.True(apk.CanRead);
        }

        [Fact]
        public async Task CompositesBothAdaptiveLayersInLauncherCoordinates()
        {
            using FileStream apk = Open("adaptive");
            using MeshNonSeekableStream stream = new(apk);
            using VideoFrame? frame = await ApkIconReader.ReadAsync(stream, 72);
            Assert.NotNull(frame);
            Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(frame, 0, 0));
            Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(frame, 17, 36));
            Assert.Equal(new byte[] { 128, 0, 127, 255 }, Pixel(frame, 18, 36));
            Assert.Equal(new byte[] { 128, 0, 127, 255 }, Pixel(frame, 53, 36));
            Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(frame, 54, 36));
        }

        [Fact]
        public async Task ReturnsNullWhenOnlyAnActivityDeclaresAnIcon()
        {
            using FileStream apk = Open("absent");
            using VideoFrame? frame = await ApkIconReader.ReadAsync(apk, 64);
            Assert.Null(frame);
        }

        [Fact]
        public async Task MissingDeclaredResourceRaisesAnError()
        {
            using FileStream original = Open("raster");
            using MemoryStream apk = new();
            await original.CopyToAsync(apk);
            using (ZipArchive archive = new(apk, ZipArchiveMode.Update, true))
            {
                archive.GetEntry("res/drawable-mdpi-v4/icon.png")!.Delete();
            }
            await Assert.ThrowsAsync<InvalidDataException>(async () => await ApkIconReader.ReadAsync(apk, 48));
        }

        [Fact]
        public async Task CancellationLeavesInputOpen()
        {
            using FileStream apk = Open("raster");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await ApkIconReader.ReadAsync(apk, 48, new CancellationToken(true)));
            Assert.True(apk.CanRead);
        }

        private static FileStream Open(string name) => File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "Apk", name + ".apk"));
        private static byte[] Pixel(VideoFrame frame, int x, int y) => frame.Data.Slice(frame.Planes[0].Offset + frame.Planes[0].Stride * y + 4 * x, 4).ToArray();
    }
}

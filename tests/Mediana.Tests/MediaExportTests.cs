// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Tests
{
    public class MediaExportTests
    {
        private static string Root => Path.Combine(AppContext.BaseDirectory, "Data");

        [Theory]
        [InlineData("Png", "c6-d16-i1-13x11.png")]
        [InlineData("Raster", "bitmap-RGB.bmp")]
        [InlineData("Raster", "icon-png.ico")]
        [InlineData("Raster", "transparent.gif")]
        [InlineData("Raster", "tiff-RGBA-tiff_lzw.tif")]
        [InlineData("Raster", "webp-gradient-4.webp")]
        [InlineData("Jpeg", "rgb-s2-1x1.jpg")]
        [InlineData("Heif", "basic-8bit.heic")]
        [InlineData("Jpeg2000", "RGB-129x101.jp2")]
        [InlineData("Jbig2", "mmr-white.jb2")]
        [InlineData("Svg", "vector-preview.svg")]
        [InlineData("Tgs", "geometry.tgs")]
        [InlineData("Pdf", "graphics.pdf")]
        public Task AllRegisteredVisualFamiliesUseTheSamePublicExport(string group, string file) => MediaExportTestInput.VerifyVisualAsync(Root, group, file);

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public Task DocumentPagesAndPresentationSlidesUseTheSamePublicExport(bool presentation) => MediaExportTestInput.VerifyDocumentAsync(presentation);

        [Theory]
        [InlineData("Wave", "f64-stereo.wav")]
        [InlineData("Aiff", "f64-stereo.aiff")]
        [InlineData("Mp3", "mono-speech.mp3")]
        [InlineData("Flac", "mono-speech.flac")]
        [InlineData("Mp4", "avc-aac.mp4")]
        [InlineData("Matroska", "avc-aac-attachment.mkv")]
        [InlineData("Ogg", "opus-stereo.ogg")]
        [InlineData("Ogg", "vorbis-stereo.ogg")]
        public Task AllRegisteredAudioFamiliesUseTheSamePcmExport(string group, string file) => MediaExportTestInput.VerifyPcmAsync(Root, group, file);

        [Theory]
        [InlineData("Raster", "webp-animation.webp")]
        [InlineData("Raster", "transparent.gif")]
        [InlineData("Tgs", "geometry.tgs")]
        [InlineData("Mp4", "avc-multislice-cabac.mp4")]
        public Task ImagesAndVideoUseTheSameAnimationExport(string group, string file) => MediaExportTestInput.VerifyAnimationAsync(Root, group, file);

        [Fact]
        public Task PublicVideoEncodingMatchesExistingFullOutput() => MediaExportTestInput.VerifyVideoEncodingAsync(Root);
        [Fact]
        public Task PublicAudioEncodingReportsFinalCodecTiming() => MediaExportTestInput.VerifyAudioEncodingAsync(Root);

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task FailureAndCancellationReleaseOutputBuffersAndKeepStreamsBorrowed(bool cancel)
        {
            MediaEngine engine = MediaExportTestInput.CreateEngine();
            using FileStream input = File.OpenRead(Path.Combine(Root, "Mp4", cancel ? "avc-multislice-cabac.mp4" : "avc-aac.mp4"));
            await using RandomAccessStreamByteSource source = new(input, leaveOpen: true);
            using MemoryStream output = new();
            using CancellationTokenSource cancellation = new();
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            if (cancel)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.TranscodeVideoAsync(source, output, "h264", "mpegts", allocator,
                    progress: _ => cancellation.Cancel(), cancellationToken: cancellation.Token));
            }
            else
            {
                await Assert.ThrowsAsync<NotSupportedException>(() => engine.TranscodeVideoAsync(source, output, "h264", "mpegts", allocator));
                Assert.Equal(0, output.Length);
            }
            Assert.Equal(0, allocator.AllocatedBytes); Assert.True(input.CanRead); Assert.True(output.CanWrite);
        }
    }
}

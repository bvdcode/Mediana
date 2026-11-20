// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using Mediana.Documents.OpenXml;
using Mediana.Documents.Presentation;
using Mediana.Drawing;
using Mediana.Tests;
using Xunit;

namespace Mediana.Documents.Tests
{
    public class PresentationStreamTests
    {
        [Fact]
        public async Task SeekableInputSkipsUnrequestedPayloadAndPreservesPixels()
        {
            byte[] input = PresentationTestData.Create(badSecond: true);
            using MemoryStream encoded = new();
            encoded.Write(input);
            using (ZipArchive zip = new(encoded, ZipArchiveMode.Update, leaveOpen: true))
            {
                await using Stream part = await zip.CreateEntry("ppt/media/unused.bin", CompressionLevel.NoCompression).OpenAsync();
                await part.WriteAsync(new byte[8 * 1024 * 1024]);
            }
            encoded.Position = encoded.Length;
            using CountingSourceStream source = new(encoded);
            using PresentationDocument streamed = PresentationReader.Open(source);
            using PresentationDocument memory = PresentationReader.Open(input);
            PresentationRenderOptions options = new() { Dpi = 72, Supersampling = 1, Text = new() { SubstituteMissingFonts = true } };
            using PixelBuffer actual = await PresentationRenderer.RenderAsync(streamed, 0, options);
            using PixelBuffer expected = await PresentationRenderer.RenderAsync(memory, 0, options);
            Assert.Equal(expected.Pixels.ToArray(), actual.Pixels.ToArray());
            Assert.True(source.BytesRead < 128 * 1024, $"Read {source.BytesRead} bytes.");
            streamed.Dispose();
            Assert.False(source.Disposed);
            Assert.Throws<ObjectDisposedException>(() => streamed.ReadText().First());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void InputOwnershipIsHonoredOnSuccessAndInitializationFailure(bool leaveOpen)
        {
            using CountingSourceStream source = new(new MemoryStream(PresentationTestData.Create()));
            PresentationReader.Open(source, leaveOpen: leaveOpen).Dispose();
            Assert.Equal(!leaveOpen, source.Disposed);
            using CountingSourceStream invalid = new(new MemoryStream(new byte[100]));
            Assert.Throws<InvalidDataException>(() => PresentationReader.Open(invalid, leaveOpen: leaveOpen));
            Assert.Equal(!leaveOpen, invalid.Disposed);
            using CountingSourceStream cancelled = new(new MemoryStream(PresentationTestData.Create()));
            using CancellationTokenSource stop = new();
            stop.Cancel();
            Assert.Throws<OperationCanceledException>(() => PresentationReader.Open(cancelled, leaveOpen: leaveOpen, cancellationToken: stop.Token));
            Assert.Equal(!leaveOpen, cancelled.Disposed);
        }

        [Fact]
        public void UnsupportedSourcesAndInputBoundsFailBeforeReading()
        {
            using CountingSourceStream nonseekable = new(new MemoryStream(PresentationTestData.Create()), false);
            Assert.Throws<ArgumentException>(() => OpenXmlPackage.Open(nonseekable));
            Assert.Equal(0, nonseekable.BytesRead);
            using CountingSourceStream tooLarge = new(new MemoryStream(PresentationTestData.Create()));
            Assert.Throws<InvalidDataException>(() => OpenXmlPackage.Open(tooLarge, new() { MaximumInputBytes = 10 }));
            Assert.Equal(0, tooLarge.BytesRead);
            Assert.False(tooLarge.Disposed);
        }
    }
}

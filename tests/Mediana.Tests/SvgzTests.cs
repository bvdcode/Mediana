// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using Mediana.Codecs;
using Mediana.Core.Video;
using Mediana.Drawing;
using Mediana.Images.Svg;
using Mediana.Images.Tgs;

namespace Mediana.Tests
{
    public class SvgzTests
    {
        [Fact]
        public async Task CompressedXmlAndPlainSvgRenderIdenticalPixelsThroughPublicReaders()
        {
            byte[] plain = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Svg", "effects-preview.svg"));
            byte[] encoded = Compress(plain);
            using MemoryStream source = new(encoded);
            using SvgDocument original = await SvgReader.ReadAsync(plain);
            using SvgDocument compressed = await SvgReader.ReadAsync(source);
            DrawingRenderOptions options = new() { Width = 200 };
            using PixelBuffer expected = SvgRenderer.Render(original, options);
            using PixelBuffer actual = SvgRenderer.Render(compressed, options);
            Assert.True(expected.Pixels.Span.SequenceEqual(actual.Pixels.Span));
            Assert.True(source.CanRead);
            Assert.Equal(SvgReader.ReadInfo(plain).Width, SvgReader.ReadInfo(encoded).Width);
            ImageDecodeContext registry = new([new TgsImageDecoder(), new SvgImageDecoder(renderOptions: options)]);
            using VideoFrame registered = await registry.DecodeAsync(encoded, new(), new(64 * 1024 * 1024));
            Assert.True(expected.Pixels.Span.SequenceEqual(registered.Data.Span));
            Assert.IsType<TgsImageDecoder>(registry.FindDecoder(Compress("{\"v\":5}"u8)));
        }

        [Fact]
        public void EnforcesExpandedSizeIntegrityAndCancellation()
        {
            byte[] plain = System.Text.Encoding.UTF8.GetBytes("<svg width='1' height='1'>" + new string(' ', 4096) + "</svg>");
            byte[] encoded = Compress(plain);
            Assert.Throws<InvalidDataException>(() => SvgReader.Read(encoded, new() { MaximumInputBytes = 512 }));
            Assert.Throws<InvalidDataException>(() => SvgReader.Read(encoded.AsMemory(0, encoded.Length - 1)));
            encoded[^8] ^= 1;
            Assert.Throws<InvalidDataException>(() => SvgReader.ReadInfo(encoded));
            Assert.Throws<OperationCanceledException>(() => SvgReader.Read(encoded, cancellationToken: new(true)));
        }

        private static byte[] Compress(ReadOnlySpan<byte> data)
        {
            using MemoryStream destination = new();
            using (GZipStream encoder = new(destination, CompressionLevel.Optimal, leaveOpen: true)) { encoder.Write(data); }
            return destination.ToArray();
        }
    }
}

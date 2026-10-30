// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text;
using System.Xml;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Drawing;
using Mediana.Images.Svg;
using Mediana.IO;

namespace Mediana.Tests
{
    public class SvgImageDecoderTests
    {
        [Theory]
        [InlineData("utf-8")]
        [InlineData("utf-16")]
        [InlineData("utf-16BE")]
        [InlineData("utf-32")]
        [InlineData("utf-32BE")]
        [InlineData("iso-8859-1")]
        public async Task ByteEncodingAndDeclaredNonAsciiTextArePreserved(string name)
        {
            Encoding encoding = name switch
            {
                "utf-8" => new UTF8Encoding(true, true),
                "utf-16" => new UnicodeEncoding(false, true, true),
                "utf-16BE" => new UnicodeEncoding(true, true, true),
                "utf-32" => new UTF32Encoding(false, true, true),
                "utf-32BE" => new UTF32Encoding(true, true, true),
                "iso-8859-1" => Encoding.Latin1,
                _ => throw new ArgumentOutOfRangeException(nameof(name))
            };
            string xml = $"<?xml version='1.0' encoding='{name}'?><svg width='20' height='20'><text x='0' y='15'>é</text></svg>";
            byte[] input = [.. encoding.GetPreamble(), .. encoding.GetBytes(xml)];
            SvgRenderOptions options = new() { Supersampling = 1 };
            PixelBuffer expected = SvgRenderer.Render(SvgReader.Read(xml), options);
            Assert.True(expected.Pixels.Span.ContainsAnyExcept((byte)0));
            SvgImageDecoder decoder = new(renderOptions: options);
            Assert.True(decoder.CanDecode(input));
            MediaBufferAllocator allocator = new(32 * 1024);
            await using ShortReadSource source = new(input);
            using (VideoFrame frame = await new MediaEngineBuilder().AddImageDecoder(decoder).Build().DecodeImageAsync(source, allocator))
            {
                Assert.Equal(expected.Pixels.ToArray(), frame.Data.ToArray());
                Assert.Equal(SampleAspectRatio.Square, frame.Format.SampleAspectRatio);
                Assert.Equal(VideoColorInfo.Srgb, frame.Format.Color);
                Assert.False(source.IsDisposed);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            using ReadOnlyMemoryStream stream = new(input);
            SvgDocument fromStream = await SvgReader.ReadAsync(stream);
            Assert.Equal(expected.Pixels.ToArray(), SvgRenderer.Render(fromStream, options).Pixels.ToArray());
            Assert.True(stream.CanRead);
        }

        [Fact]
        public async Task LimitsUnknownRootAndExternalEntitiesRemainExplicit()
        {
            byte[] input = "<svg width='4' height='4'/>"u8.ToArray();
            MediaBufferAllocator allocator = new(1024);
            await Assert.ThrowsAsync<InvalidDataException>(async () => await new SvgImageDecoder(new() { MaximumInputBytes = 1 }).DecodeAsync(input, new(), allocator));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await new SvgImageDecoder(renderOptions: new() { MaximumSamples = 1 }).DecodeAsync(input, new(), allocator));
            await Assert.ThrowsAsync<InvalidDataException>(async () => await new SvgImageDecoder().DecodeAsync("<html/>"u8.ToArray(), new(), allocator));
            byte[] external = "<!DOCTYPE svg [<!ENTITY external SYSTEM 'file:///unavailable'>]><svg width='1' height='1'><text>&external;</text></svg>"u8.ToArray();
            await Assert.ThrowsAsync<XmlException>(async () => await new SvgImageDecoder().DecodeAsync(external, new(), allocator));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await new SvgImageDecoder().DecodeAsync(input, new(), allocator, new CancellationToken(true)));
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}

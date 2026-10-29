// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Drawing;
using Mediana.Images.Svg;

namespace Mediana.Tests
{
    public class SvgTests
    {
        [Fact]
        public async Task FilterReferencesAndBundledFontsRender()
        {
            await SvgReferenceTestInput.VerifyAllAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Svg"));
        }

        [Fact]
        public void RenderedFrameHasIndependentPixelsAndSrgbMetadata()
        {
            SvgDocument document = SvgReader.Read("<svg width='2' height='1'><rect width='1' height='1' fill='#ff0000'/></svg>");
            PixelBuffer pixels = SvgRenderer.Render(document, new SvgRenderOptions { Supersampling = 1 });
            MediaBufferAllocator allocator = new(8);
            using VideoFrame frame = pixels.ToVideoFrame(allocator);
            Assert.Equal(new byte[] { 255, 0, 0, 255, 0, 0, 0, 0 }, frame.Data.ToArray());
            pixels.Clear(new Rgba32(0, 0, 255));
            Assert.Equal(255, frame.Data.Span[0]);
            Assert.Equal(VideoColorInfo.Srgb, frame.Format.Color);
            Assert.Equal(SampleAspectRatio.Square, frame.Format.SampleAspectRatio);
            frame.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void UnsupportedFeaturesAndLimitsAreExplicit()
        {
            SvgDocument document = SvgReader.Read("<svg width='32' height='32'><rect width='32' height='32'/></svg>");
            Assert.Throws<ArgumentOutOfRangeException>(() => SvgRenderer.Render(document, new() { MaximumSamples = 10 }));
            Assert.Throws<OperationCanceledException>(() => SvgRenderer.Render(document, cancellationToken: new CancellationToken(true)));
            Assert.Throws<InvalidDataException>(() => SvgReader.Read("<svg/>", new() { MaximumInputBytes = 1 }));
            SvgDocument unsupported = SvgReader.Read("<svg width='1' height='1'><foreignObject width='1' height='1'/></svg>");
            Assert.NotEmpty(unsupported.UnsupportedFeatures);
            Assert.Throws<NotSupportedException>(() => SvgRenderer.Render(unsupported));
        }

        [Fact]
        public void ExternalEntitiesAreNotResolved()
        {
            Assert.Throws<System.Xml.XmlException>(() => SvgReader.Read(
                "<!DOCTYPE svg [<!ENTITY external SYSTEM 'file:///unavailable'>]><svg width='1' height='1'><text>&external;</text></svg>"));
        }
    }
}

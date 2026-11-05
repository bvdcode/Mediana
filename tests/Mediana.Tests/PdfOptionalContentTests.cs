// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.Pdf;
using Mediana.Drawing;

namespace Mediana.Tests
{
    public class PdfOptionalContentTests
    {
        [Theory]
        [InlineData("/ON", "", true)]
        [InlineData("/OFF", "", false)]
        [InlineData("/OFF", "/ON [5 0 R]", true)]
        [InlineData("/ON", "/OFF [5 0 R]", false)]
        [InlineData("/OFF", "/Intent []", true)]
        public async Task DefaultConfigurationControlsMarkedContent(string state, string entries, bool visible)
        {
            using PdfDocument document = await Read(state, entries, "/OC /Layer BDC 0 1 0 rg 0 0 100 100 re f EMC");
            using PixelBuffer pixels = PdfRenderer.Render(document, 1, new() { Dpi = 72, Supersampling = 1 });
            Assert.Equal(visible ? new Rgba32(0, 255, 0) : new Rgba32(255, 255, 255), pixels[20, 20]);
        }

        [Fact]
        public async Task HiddenPaintPreservesGraphicsStateAndNestedVisibility()
        {
            using PdfDocument document = await Read("/OFF", "", "/OC /Layer BDC 0 1 0 rg 0 0 100 100 re f /Span BMC 1 0 0 rg 0 0 10 10 re f EMC EMC 0 0 100 100 re f");
            using PixelBuffer pixels = PdfRenderer.Render(document, 1, new() { Dpi = 72, Supersampling = 1 });
            Assert.Equal(new Rgba32(255, 0, 0), pixels[20, 20]);
        }

        [Theory]
        [InlineData("/AnyOn", true)]
        [InlineData("/AllOn", false)]
        [InlineData("/AnyOff", true)]
        [InlineData("/AllOff", false)]
        public async Task MembershipPoliciesUseAllGroupStates(string policy, bool visible)
        {
            using PdfDocument document = await Read("/OFF", "/ON [5 0 R]", "/OC << /Type /OCMD /OCGs [5 0 R 6 0 R] /P " + policy + " >> BDC 1 0 0 rg 0 0 100 100 re f EMC", second: true);
            using PixelBuffer pixels = PdfRenderer.Render(document, 1, new() { Dpi = 72, Supersampling = 1 });
            Assert.Equal(visible ? new Rgba32(255, 0, 0) : new Rgba32(255, 255, 255), pixels[20, 20]);
        }

        [Fact]
        public async Task VisibilityExpressionsAndViewUsageAreEvaluated()
        {
            using PdfDocument document = await Read("/ON", "/AS [<< /Event /View /Category [/View] /OCGs [5 0 R] >>]", "/OC << /Type /OCMD /VE [/Not 5 0 R] >> BDC 0 1 0 rg 0 0 100 100 re f EMC", group: "/Usage << /View << /ViewState /OFF >> >>");
            using PixelBuffer pixels = PdfRenderer.Render(document, 1, new() { Dpi = 72, Supersampling = 1 });
            Assert.Equal(new Rgba32(0, 255, 0), pixels[20, 20]);
        }

        [Theory]
        [InlineData("EMC")]
        [InlineData("/OC /Layer BDC")]
        [InlineData("/OC << /Type /OCMD /VE [/Not 7 0 R] >> BDC EMC")]
        public async Task UnbalancedOrCyclicMembershipIsRejected(string content)
        {
            using PdfDocument document = await Read("/ON", "", content);
            Assert.Throws<InvalidDataException>(() => document.GetPage(1));
        }

        private static Task<PdfDocument> Read(string state, string entries, string content, string group = "", bool second = false)
        {
            List<byte[]> objects =
            [
                PdfTestDocument.Bytes("<< /Type /Catalog /Pages 2 0 R /OCProperties << /OCGs [5 0 R " + (second ? "6 0 R" : "") + "] /D << /BaseState " + state + " " + entries + " >> >> >>"),
                PdfTestDocument.Bytes("<< /Type /Pages /Count 1 /Kids [3 0 R] >>"),
                PdfTestDocument.Bytes("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 100] /Resources << /Properties << /Layer 5 0 R >> >> /Contents 4 0 R >>"),
                PdfTestDocument.Stream(PdfTestDocument.Bytes(content)),
                PdfTestDocument.Bytes("<< /Type /OCG /Name (Layer) " + group + " >>"),
                PdfTestDocument.Bytes("<< /Type /OCG /Name (Other layer) >>"),
                PdfTestDocument.Bytes("<< /Type /OCMD /VE [/Not 7 0 R] >>")
            ];
            return PdfReader.ReadAsync(new MemoryStream(PdfTestDocument.Write(objects)));
        }
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.Pdf;
using Mediana.Drawing;

namespace Mediana.Tests
{
    public class PdfShadingTests
    {
        private const string RgbFunction = "<< /FunctionType 2 /Domain [0 1] /C0 [1 0 0] /C1 [0 0 1] /N 1 >>";

        [Theory]
        [InlineData("", 128)]
        [InlineData("/BBox [50 0 150 100]", 255)]
        public async Task AxialShadingHonorsColorAndBounds(string bounds, int outsideRed)
        {
            using PdfDocument document = await ReadAsync("<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [0 0 200 0] /Function " + RgbFunction + " " + bounds + " >>");
            using PixelBuffer image = PdfRenderer.Render(document, 1, new() { Dpi = 72, Supersampling = 1 });
            Assert.InRange(image[100, 50].R, (byte)125, (byte)130);
            Assert.InRange(image[100, 50].B, (byte)125, (byte)130);
            Assert.Equal(0, image[100, 50].G);
            if (outsideRed == 255) { Assert.Equal(new Rgba32(255, 255, 255), image[20, 50]); }
            else { Assert.InRange(image[20, 50].R, (byte)225, (byte)232); }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task RadialShadingOnlyExtendsWhenDeclared(bool extend)
        {
            using PdfDocument document = await ReadAsync("<< /ShadingType 3 /ColorSpace /DeviceRGB /Coords [100 50 0 100 50 50] /Extend [false "
                + (extend ? "true" : "false") + "] /Function " + RgbFunction + " >>");
            using PixelBuffer image = PdfRenderer.Render(document, 1, new() { Dpi = 72, Supersampling = 1 });
            Assert.InRange(image[100, 50].R, (byte)248, (byte)255);
            Assert.InRange(image[140, 50].B, (byte)203, (byte)210);
            Assert.Equal(extend ? new Rgba32(0, 0, 255) : new Rgba32(255, 255, 255), image[180, 50]);
        }

        [Fact]
        public async Task StitchingFunctionSelectsAndEncodesEachInterval()
        {
            const string function = "<< /FunctionType 3 /Domain [0 1] /Bounds [0.5] /Encode [0 1 0 1] /Functions ["
                + "<< /FunctionType 2 /Domain [0 1] /C0 [0] /C1 [1] /N 1 >> "
                + "<< /FunctionType 2 /Domain [0 1] /C0 [1] /C1 [0] /N 1 >>] >>";
            using PdfDocument document = await ReadAsync("<< /ShadingType 2 /ColorSpace /DeviceGray /Coords [0 0 200 0] /Function " + function + " >>");
            using PixelBuffer image = PdfRenderer.Render(document, 1, new() { Dpi = 72, Supersampling = 1 });
            Assert.InRange(image[50, 50].R, (byte)125, (byte)132);
            Assert.InRange(image[100, 50].R, (byte)250, (byte)255);
            Assert.InRange(image[150, 50].R, (byte)123, (byte)130);
        }

        [Fact]
        public async Task ComponentCountAndEvaluationBudgetAreValidated()
        {
            string shading = "<< /ShadingType 2 /ColorSpace /DeviceGray /Coords [0 0 200 0] /Function " + RgbFunction + " >>";
            using PdfDocument invalid = await ReadAsync(shading);
            Assert.Throws<InvalidDataException>(() => PdfRenderer.Render(invalid, 1));
            using PdfDocument limited = await ReadAsync(shading.Replace("/DeviceGray", "/DeviceRGB", StringComparison.Ordinal), new() { MaximumOperations = 1000 });
            Assert.Throws<InvalidDataException>(() => PdfRenderer.Render(limited, 1));
        }

        private static Task<PdfDocument> ReadAsync(string shading, PdfReaderOptions? options = null) =>
            PdfReader.ReadAsync(new MemoryStream(PdfTestDocument.Create("/S sh", resources: "/Shading << /S 5 0 R >>", extraObjects: PdfTestDocument.Bytes(shading))), options);
    }
}

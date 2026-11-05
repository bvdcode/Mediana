// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Documents.Pdf;

namespace Mediana.Tests
{
    public class PdfJpeg2000Tests
    {
        [Theory]
        [InlineData("RGB-17x1", "DeviceRGB", 17, 1, 3)]
        [InlineData("L-63x45", "DeviceGray", 63, 45, 1)]
        public async Task ExplicitPdfColorSpaceUsesDecodedComponentsAndIgnoresDecode(string name, string space, int width, int height, int components)
        {
            byte[] encoded = await File.ReadAllBytesAsync(Data(name + ".jp2"));
            byte[] expected = await File.ReadAllBytesAsync(Data(name + ".raw"));
            byte[] bytes = Image(encoded, space, width, height, "/Decode [1 0]");
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(bytes));
            using PdfPage page = document.GetPage(1);
            ReadOnlyMemory<byte> pixels = page.Images.Single().Pixels;
            for (int index = 0; index < width * height; index++)
            {
                for (int component = 0; component < 3; component++)
                {
                    Assert.Equal(expected[index * components + Math.Min(component, components - 1)], pixels.Span[index * 4 + component]);
                }
                Assert.Equal(255, pixels.Span[index * 4 + 3]);
            }
        }

        [Fact]
        public async Task PdfColorSpaceOverridesConflictingJp2ColorMetadata()
        {
            byte[] encoded = await File.ReadAllBytesAsync(Data("RGB-1x1.jp2"));
            int colorBox = encoded.AsSpan().IndexOf("colr"u8);
            Assert.True(colorBox >= 0);
            BinaryPrimitives.WriteUInt32BigEndian(encoded.AsSpan(colorBox + 7), 17);
            byte[] expected = await File.ReadAllBytesAsync(Data("RGB-1x1.raw"));
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(Image(encoded, "DeviceRGB", 1, 1)));
            using PdfPage page = document.GetPage(1);
            Assert.Equal(expected, page.Images.Single().Pixels[..3].ToArray());
        }

        [Fact]
        public async Task ColorComponentMismatchIsRejectedAndColorKeysAreApplied()
        {
            byte[] encoded = await File.ReadAllBytesAsync(Data("L-1x1.jp2"));
            using (PdfDocument invalid = await PdfReader.ReadAsync(new MemoryStream(Image(encoded, "DeviceRGB", 1, 1))))
            {
                Assert.Throws<InvalidDataException>(() => invalid.GetPage(1));
            }
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(Image(encoded, "DeviceGray", 1, 1, "/Mask [0 255]")));
            using PdfPage page = document.GetPage(1);
            Assert.Equal(0, page.Images.Single().Pixels.Span[3]);
        }

        private static string Data(string name) => Path.Combine(AppContext.BaseDirectory, "Data", "Jpeg2000", name);
        private static byte[] Image(byte[] encoded, string space, int width, int height, string entries = "") =>
            PdfTestDocument.Create("/I Do", resources: "/XObject << /I 5 0 R >>", extraObjects: PdfTestDocument.Stream(encoded,
                $"/Type /XObject /Subtype /Image /Width {width} /Height {height} /ColorSpace /{space} /Filter /JPXDecode " + entries));
    }
}

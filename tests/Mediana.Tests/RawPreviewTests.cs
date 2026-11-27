// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Codecs.Jpeg;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Images.Metadata;

namespace Mediana.Tests
{
    public class RawPreviewTests
    {
        private static string Data(string file) => Path.Combine(AppContext.BaseDirectory, "Data", file);

        [Theory]
        [InlineData(1, false)]
        [InlineData(7, false)]
        [InlineData(8, true)]
        [InlineData(35, true)]
        [InlineData(2560, true)]
        public async Task SelectsOriginalJpegByRequestedSize(int size, bool large)
        {
            byte[] smallJpeg = await File.ReadAllBytesAsync(Data("Raster/metadata-preview.jpg"));
            byte[] largeJpeg = await File.ReadAllBytesAsync(Data("Jpeg/rgb-s0-35x19.jpg"));
            byte[] raw = await CameraFile(largeJpeg);
            using MemoryStream stream = new(raw);
            RawImagePreview? preview = await RawPreviewReader.ExtractAsync(stream, size);
            Assert.NotNull(preview);
            raw.AsSpan().Clear();
            Assert.True(stream.CanRead);
            Assert.Equal(large ? largeJpeg : smallJpeg, preview.Encoded.ToArray());
            using VideoFrame frame = preview.Decode(new(8 * 1024 * 1024));
            Assert.Equal(large ? 19 : 5, frame.Format.Width);
            Assert.Equal(large ? 35 : 7, frame.Format.Height);
            using VideoFrame sized = preview.DecodeForPreview(new(8 * 1024 * 1024));
            (int width, int height) = size switch
            {
                1 => (2, 2),
                8 => (5, 9),
                _ => (frame.Format.Width, frame.Format.Height)
            };
            Assert.Equal((width, height), (sized.Format.Width, sized.Format.Height));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(6)]
        [InlineData(8)]
        public async Task JpegOrientationOverridesParentEvenWhenExplicitlyUpright(byte orientation)
        {
            byte[] jpeg = await File.ReadAllBytesAsync(Data("Jpeg/rgb-s0-35x19.jpg"));
            byte[] tagged = JpegTestData.Insert(jpeg, 225,
                [.. "Exif\0\0II"u8, 42, 0, 8, 0, 0, 0, 1, 0, 18, 1, 3, 0, 1, 0, 0, 0, orientation, 0, 0, 0, 0, 0, 0, 0]);
            using MemoryStream stream = new(await CameraFile(tagged));
            RawImagePreview? preview = await RawPreviewReader.ExtractAsync(stream, 35);
            Assert.NotNull(preview);
            MediaBufferAllocator allocator = new(8 * 1024 * 1024);
            using VideoFrame expected = await new JpegImageDecoder().DecodeAsync(tagged, new(), allocator);
            using VideoFrame actual = preview.Decode(allocator);
            Assert.Equal((expected.Format.Width, expected.Format.Height), (actual.Format.Width, actual.Format.Height));
            Assert.True(expected.Data.Span.SequenceEqual(actual.Data.Span));
        }

        [Fact]
        public async Task MissingPreviewAndInvalidExtentHaveDistinctOutcomes()
        {
            byte[] raf = await File.ReadAllBytesAsync(Data("Raster/metadata-sensor.raf"));
            raf.AsSpan(84, 8).Clear();
            using MemoryStream source = new(raf);
            Assert.Null(await RawPreviewReader.ExtractAsync(source, 200));
            source.Position = 0;
            raf[84] = 0xff;
            await Assert.ThrowsAsync<InvalidDataException>(async () => await RawPreviewReader.ExtractAsync(source, 200));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await RawPreviewReader.ExtractAsync(source, 200, cancellationToken: new(true)));
        }

        [Fact]
        public async Task SequentialSourceAndInputLimitAreSupported()
        {
            byte[] data = await File.ReadAllBytesAsync(Data("Raster/metadata-sensor.raf"));
            using ShortReadStream bytes = new(data);
            using MeshNonSeekableStream source = new(bytes);
            Assert.NotNull(await RawPreviewReader.ExtractAsync(source, 200, data.Length));
            bytes.Position = 0;
            await Assert.ThrowsAsync<InvalidDataException>(async () => await RawPreviewReader.ExtractAsync(source, 200, data.Length - 1));
        }

        [Fact]
        public async Task PanasonicPreviewFieldEndsAtParsedJpegBoundary()
        {
            byte[] data = await File.ReadAllBytesAsync(Data("Raster/metadata-sensor-rw2-le.rw2"));
            byte[] jpeg = await File.ReadAllBytesAsync(Data("Raster/metadata-preview.jpg"));
            int count = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(24));
            for (int i = 0; i < count; i++)
            {
                int entry = 26 + i * 12;
                if (BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(entry)) == 46)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(entry + 4), jpeg.Length + 4);
                    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(entry + 8), data.Length);
                    break;
                }
            }
            using MemoryStream stream = new([.. data, .. jpeg, 0xff, 0xd9, 0, 0]);
            RawImagePreview? preview = await RawPreviewReader.ExtractAsync(stream, 200);
            Assert.NotNull(preview);
            Assert.Equal(jpeg, preview.Encoded.ToArray());
        }

        [Fact]
        public async Task SkipsBadCandidatesInEitherOrderAndStopsAtRealEndMarker()
        {
            byte[] jpeg = await File.ReadAllBytesAsync(Data("Jpeg/rgb-s0-35x19.jpg"));
            byte[] marked = JpegTestData.Insert(jpeg, 254, [1, 255, 217, 2]);
            byte[] raw = await CameraFile([.. marked, 0, 1, 255, 217]);
            byte[] small = await File.ReadAllBytesAsync(Data("Raster/metadata-preview.jpg"));
            raw[raw.AsSpan().IndexOf(small)] = 0;
            using MemoryStream source = new(raw);
            RawImagePreview? preview = await RawPreviewReader.ExtractAsync(source, 35);
            Assert.NotNull(preview);
            Assert.Equal(marked, preview.Encoded.ToArray());
            using VideoFrame frame = preview.DecodeForPreview(new(16384));
            Assert.Equal((19, 35), (frame.Format.Width, frame.Format.Height));

            using MemoryStream badLast = new(await CameraFile([255, 216, 255]));
            Assert.Equal(small, (await RawPreviewReader.ExtractAsync(badLast, 35))!.Encoded.ToArray());
            BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(206 + 4 * 12 + 2), 0);
            source.Position = 0;
            Assert.Equal(marked, (await RawPreviewReader.ExtractAsync(source, 35))!.Encoded.ToArray());
            BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(206 + 4 * 12 + 2), 4);
            raw[raw.AsSpan().IndexOf(marked)] = 0;
            source.Position = 0;
            await Assert.ThrowsAsync<InvalidDataException>(async () => await RawPreviewReader.ExtractAsync(source, 35));
        }

        [Fact]
        public async Task InfersMarkerlessColorInsideRawAndPreservesOrientedPixels()
        {
            byte[] jpeg = await File.ReadAllBytesAsync(Data("Jpeg/rgb-s0-35x19.jpg"));
            int app = JpegTestData.Find(jpeg, 224);
            int end = app + 2 + BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(app + 2));
            byte[] unmarked = [.. jpeg.AsSpan(0, app), .. jpeg.AsSpan(end)];
            Assert.Throws<NotSupportedException>(() => new JpegImageDecoder().Decode(unmarked, new(), new(16384)));
            using MemoryStream source = new(await CameraFile(unmarked));
            RawImagePreview? preview = await RawPreviewReader.ExtractAsync(source, 35);
            Assert.NotNull(preview);
            Assert.Equal(unmarked, preview.Encoded.ToArray());
            using VideoFrame actual = preview.DecodeForPreview(new(16384));
            using VideoFrame expected = await new JpegImageDecoder(new() { ContainerExifOrientation = 6 }).DecodeAsync(jpeg, new(), new(16384));
            Assert.True(actual.Data.Span.SequenceEqual(expected.Data.Span));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task PreviewPhotometricResolvesColorWhileJpegDeclarationsTakePrecedence(bool keepJfif)
        {
            byte[] original = await File.ReadAllBytesAsync(Data("Jpeg/rgb-s0-35x19.jpg"));
            int app = JpegTestData.Find(original, 224);
            int end = app + 2 + BinaryPrimitives.ReadUInt16BigEndian(original.AsSpan(app + 2));
            byte[] jpeg = keepJfif ? original : [.. original.AsSpan(0, app), .. original.AsSpan(end)];
            using MemoryStream source = new(await CameraFile(jpeg, 2));
            RawImagePreview? preview = await RawPreviewReader.ExtractAsync(source, 35);
            Assert.NotNull(preview);
            using VideoFrame actual = preview.DecodeForPreview(new(16384));
            JpegImageDecoder reference = new(new() { ContainerExifOrientation = 6 },
                keepJfif ? JpegColorSpace.YCbCr : JpegColorSpace.Rgb);
            using VideoFrame expected = await reference.DecodeAsync(jpeg, new(), new(16384));
            Assert.True(actual.Data.Span.SequenceEqual(expected.Data.Span));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task PreviewExtractionIgnoresUnrelatedDamagedFields(bool dng)
        {
            byte[] raw = await File.ReadAllBytesAsync(Data("Raster/metadata-sensor-srw-le.srw"));
            if (dng)
            {
                // Replace Model with DNGVersion; DateTimeOriginal ends at byte 203.
                int model = 10 + 5 * 12;
                WriteEntry(raw.AsSpan(model), 50706, 1);
                raw[203] = (byte)'X';
            }
            else
            {
                BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(10 + 5 * 12 + 2), 0);
            }
            Assert.Throws(dng ? typeof(InvalidDataException) : typeof(NotSupportedException), () => new TiffImageMetadataReader().Inspect(raw));
            using MemoryStream source = new(raw);
            RawImagePreview? preview = await RawPreviewReader.ExtractAsync(source, 200);
            Assert.NotNull(preview);
            using VideoFrame frame = preview.DecodeForPreview(new(8192));
            Assert.Equal((5, 7), (frame.Format.Width, frame.Format.Height));

            BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(206 + 4 * 12 + 2), 0);
            source.Position = 0;
            await Assert.ThrowsAsync<NotSupportedException>(async () => await RawPreviewReader.ExtractAsync(source, 200));
        }

        [Fact]
        public async Task PentaxSpaceSignatureUsesMakerRelativePreviewOffsets()
        {
            byte[] aoc = await File.ReadAllBytesAsync(Data("Raster/metadata-maker-pentax.raw"));
            byte[] jpeg = await File.ReadAllBytesAsync(Data("Raster/metadata-preview.jpg"));
            const int note = 124;
            byte[] raw = [.. aoc.AsSpan(0, note), .. "PENTAX \0II"u8, .. aoc.AsSpan(note + 6)];
            BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(112), 40);
            int previewStart = raw.AsSpan().IndexOf(jpeg);
            int directory = note + 10;
            for (int i = 0; i < 2; i++)
            {
                int entry = directory + 2 + 12 * i;
                if (BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(entry)) == 4)
                { BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(entry + 8), previewStart - note); }
            }
            using MemoryStream source = new(raw);
            Assert.Equal(jpeg, (await RawPreviewReader.ExtractAsync(source, 200))!.Encoded.ToArray());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task CanonSensorIsExcludedAndAnActualPredictivePreviewDecodes(bool withPreview)
        {
            byte[] jpeg = await File.ReadAllBytesAsync(Data("Jpeg/Lossless/rgb-p4-8-0.jpg"));
            const int root = 16, sensor = 64, pixels = 132;
            byte[] raw = new byte[pixels + jpeg.Length];
            "II"u8.CopyTo(raw);
            BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(2), 42);
            BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(4), root);
            "CR\x02\0"u8.CopyTo(raw.AsSpan(8));
            BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(12), sensor);
            BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(root), (ushort)(withPreview ? 2 : 0));
            if (withPreview)
            {
                WriteEntry(raw.AsSpan(root + 2), 513, pixels);
                WriteEntry(raw.AsSpan(root + 14), 514, jpeg.Length);
            }
            BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(root + (withPreview ? 26 : 2)), sensor);
            BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(sensor), 5);
            WriteEntry(raw.AsSpan(sensor + 2), 256, 6000);
            WriteEntry(raw.AsSpan(sensor + 14), 257, 4000);
            WriteEntry(raw.AsSpan(sensor + 26), 259, 6);
            WriteEntry(raw.AsSpan(sensor + 38), 273, pixels);
            WriteEntry(raw.AsSpan(sensor + 50), 279, jpeg.Length);
            jpeg.CopyTo(raw, pixels);
            using MemoryStream source = new(raw);
            RawImagePreview? preview = await RawPreviewReader.ExtractAsync(source, 200);
            if (withPreview)
            {
                Assert.NotNull(preview);
                Assert.Equal(jpeg, preview.Encoded.ToArray());
                using VideoFrame actual = preview.DecodeForPreview(new(4096));
                using VideoFrame expected = await new JpegImageDecoder().DecodeAsync(jpeg, new(), new(4096));
                Assert.True(actual.Data.Span.SequenceEqual(expected.Data.Span));
            }
            else { Assert.Null(preview); }
        }

        private static async Task<byte[]> CameraFile(byte[] secondJpeg, ushort? photometric = null)
        {
            byte[] source = await File.ReadAllBytesAsync(Data("Raster/metadata-sensor-pef-le.pef"));
            // Append a second preview IFD after the original PEF's 7 x 5 preview.
            int directory = (source.Length + 1) & ~1;
            ushort entries = (ushort)(photometric.HasValue ? 3 : 2);
            int directoryBytes = 2 + entries * 12 + 4;
            byte[] raw = new byte[directory + directoryBytes + secondJpeg.Length];
            source.CopyTo(raw, 0);
            BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(204 + 2 + 6 * 12), directory);
            BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(directory), entries);
            int entry = directory + 2;
            if (photometric is ushort color)
            {
                WriteEntry(raw.AsSpan(entry), 262, color);
                entry += 12;
            }
            WriteEntry(raw.AsSpan(entry), 513, directory + directoryBytes);
            WriteEntry(raw.AsSpan(entry + 12), 514, secondJpeg.Length);
            secondJpeg.CopyTo(raw, directory + directoryBytes);
            return raw;
        }

        private static void WriteEntry(Span<byte> entry, ushort tag, int value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(entry, tag);
            BinaryPrimitives.WriteUInt16LittleEndian(entry[2..], 4);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[4..], 1);
            BinaryPrimitives.WriteInt32LittleEndian(entry[8..], value);
        }
    }
}

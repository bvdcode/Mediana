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

        private static async Task<byte[]> CameraFile(byte[] secondJpeg)
        {
            byte[] source = await File.ReadAllBytesAsync(Data("Raster/metadata-sensor-pef-le.pef"));
            // Append a second preview IFD after the original PEF's 7 x 5 preview.
            int directory = (source.Length + 1) & ~1;
            const int directoryBytes = 2 + 2 * 12 + 4;
            byte[] raw = new byte[directory + directoryBytes + secondJpeg.Length];
            source.CopyTo(raw, 0);
            BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(204 + 2 + 6 * 12), directory);
            BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(directory), 2);
            WriteEntry(raw.AsSpan(directory + 2), 513, directory + directoryBytes);
            WriteEntry(raw.AsSpan(directory + 14), 514, secondJpeg.Length);
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

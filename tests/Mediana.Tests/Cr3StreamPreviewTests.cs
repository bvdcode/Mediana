// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Images.Metadata;

namespace Mediana.Tests
{
    public class Cr3StreamPreviewTests
    {
        private static Task<byte[]> ReadAsync(string file) => File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", file));

        [Theory]
        [InlineData(200, true)]
        [InlineData(7, false)]
        public async Task SelectsUsingActualJpegDimensionsAndSeeksPastUnneededData(int size, bool large)
        {
            byte[] raw = await ReadAsync("Raster/metadata-sensor.cr3");
            byte[] small = await ReadAsync("Raster/metadata-preview.jpg");
            byte[] jpeg = await ReadAsync("Jpeg/rgb-s0-35x19.jpg");
            byte[] largeJpeg = JpegTestData.Insert(jpeg, 254, new byte[60_000]);
            int mdat = raw.AsSpan().IndexOf("mdat"u8);
            int sampleSize = raw.AsSpan().IndexOf("stsz"u8) + 8;
            int end = mdat + 4 + largeJpeg.Length;
            byte[] camera = new byte[end + 2 * 1024 * 1024];
            raw.AsSpan(0, mdat + 4).CopyTo(camera);
            largeJpeg.CopyTo(camera, mdat + 4);
            BinaryPrimitives.WriteInt32BigEndian(camera.AsSpan(mdat - 4), camera.Length - mdat + 4);
            BinaryPrimitives.WriteInt32BigEndian(camera.AsSpan(sampleSize), largeJpeg.Length);
            const int origin = 19;
            using MeteredReadStream stream = new([.. new byte[origin], .. camera]);
            stream.Position = origin;
            RawImagePreview? preview = await RawPreviewReader.ExtractAsync(stream, size, camera.Length);
            Assert.NotNull(preview);
            Assert.Equal(large ? largeJpeg : small, preview.Encoded.ToArray());
            Assert.Equal(stream.Length, stream.Position);
            Assert.InRange(stream.BytesRead, 1, (large ? largeJpeg.Length : small.Length) + 16_384);
            stream.Position = origin;
            await Assert.ThrowsAsync<InvalidDataException>(async () => await RawPreviewReader.ExtractAsync(stream, size, camera.Length - 1));
        }

        [Theory]
        [InlineData("co64")]
        [InlineData("PRVW")]
        [InlineData("mdat")]
        public async Task RejectsInvalidSampleExtentsAndJpegDeclarations(string box)
        {
            byte[] raw = await ReadAsync("Raster/metadata-sensor.cr3");
            switch (box)
            {
                case "co64":
                    int offset = raw.AsSpan().IndexOf("co64"u8) + 12;
                    BinaryPrimitives.WriteInt64BigEndian(raw.AsSpan(offset), raw.Length);
                    break;
                case "PRVW":
                    int width = raw.AsSpan().IndexOf("PRVW"u8) + 10;
                    BinaryPrimitives.WriteUInt16BigEndian(raw.AsSpan(width), 1);
                    break;
                case "mdat":
                    raw[^1] = 0;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(box));
            }
            using MemoryStream stream = new(raw);
            await Assert.ThrowsAsync<InvalidDataException>(async () => await RawPreviewReader.ExtractAsync(stream, 200));
        }
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Codecs.Bmp;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.Raster.Tests
{
    public class EmbeddedImageRegistryTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task NestedImagesUseTheRegisteredAsyncDecoderAndSharedDepth(bool bitmap)
        {
            byte[] icon = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "icon-png.ico"));
            byte[] input = bitmap ? WrapBitmap(icon) : icon;
            IImageDecoder outer = bitmap ? new BmpImageDecoder() : new IcoImageDecoder();
            RegisteredPngDecoder png = new();
            MediaBufferAllocator allocator = new(1024 * 1024);
            ImageDecodeContext context = new([outer, png]);
            using (VideoFrame frame = await context.DecodeAsync(input, new(), allocator))
            {
                Assert.Equal(1, png.Calls);
                Assert.True(frame.Format.Width > 0);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            await Assert.ThrowsAsync<NotSupportedException>(async () =>
                await new ImageDecodeContext([outer]).DecodeAsync(input, new(), allocator));
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await new ImageDecodeContext([outer, png], maximumDepth: 1).DecodeAsync(input, new(), allocator));
            Assert.Equal(1, png.Calls);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static byte[] WrapBitmap(byte[] icon)
        {
            int length = BinaryPrimitives.ReadInt32LittleEndian(icon.AsSpan(14));
            int offset = BinaryPrimitives.ReadInt32LittleEndian(icon.AsSpan(18));
            byte[] data = new byte[54 + length];
            data[0] = (byte)'B'; data[1] = (byte)'M';
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(2), data.Length);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(10), 54);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(14), 40);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(18), icon[6] == 0 ? 256 : icon[6]);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(22), icon[7] == 0 ? 256 : icon[7]);
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(26), 1);
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(28), 24);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(30), 5);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(34), length);
            icon.AsSpan(offset, length).CopyTo(data.AsSpan(54));
            return data;
        }
    }
}

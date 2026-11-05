// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Images.Heif;
using Mediana.Images.Heif.Container;

namespace Mediana.Tests
{
    public class HeifCompositionTests
    {
        [Fact]
        public async Task OverlayUsesCallerStorageAndPreservesClippingAndStraightAlpha()
        {
            byte[] input = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Heif", "basic-8bit.heic"));
            HeifContainer source = new HeifContainerReader(HeicDecodeOptions.Default).Read(input);
            ImageProperties properties = ImageProperties.Read(source, source.Items[source.PrimaryItemId]);
            byte[] overlay = new byte[22];
            BinaryPrimitives.WriteUInt16BigEndian(overlay.AsSpan(10), (ushort)properties.Width);
            BinaryPrimitives.WriteUInt16BigEndian(overlay.AsSpan(12), (ushort)properties.Height);
            BinaryPrimitives.WriteInt16BigEndian(overlay.AsSpan(14), -1);
            BinaryPrimitives.WriteInt16BigEndian(overlay.AsSpan(18), 1);
            BinaryPrimitives.WriteInt16BigEndian(overlay.AsSpan(20), 1);
            HeifItem item = new(1, HeifBox.Code("iovl"), false, 0, 0, [new ItemExtent(0, (ulong)overlay.Length)], [],
                new Dictionary<uint, IReadOnlyList<uint>> { [HeifBox.Code("dimg")] = new uint[] { 2, 3 } });
            HeifContainer container = new(overlay, ReadOnlyMemory<byte>.Empty, 1, new() { [1] = item }, [], overlay.Length);
            int length = properties.Width * properties.Height * 4;
            byte[] guarded = new byte[length + 2];
            Array.Fill(guarded, (byte)0xa5);
            Memory<byte> destination = guarded.AsMemory(1, length);
            using (PixelBufferScope buffers = new(true))
            {
                RgbaImage image = await OverlayImageDecoder.DecodeAsync(container, item, properties, DecodeTile, buffers, default, destination);
                Assert.True(image.Pixels.Equals((ReadOnlyMemory<byte>)destination));
                for (int y = 0; y < image.Height; y++)
                {
                    for (int x = 0; x < image.Width; x++)
                    {
                        bool red = x < 2 && y < 2;
                        bool blue = x >= 1 && x < 4 && y >= 1 && y < 3;
                        byte[] expected = (red, blue) switch
                        {
                            (true, true) => [85, 0, 170, 192],
                            (true, false) => [255, 0, 0, 128],
                            (false, true) => [0, 0, 255, 128],
                            (false, false) => [0, 0, 0, 0]
                        };
                        Assert.True(image.Pixels.Span.Slice((y * image.Width + x) * 4, 4).SequenceEqual(expected));
                    }
                }
            }
            Assert.Equal(0xa5, guarded[0]);
            Assert.Equal(0xa5, guarded[^1]);

            static Task<RgbaImage> DecodeTile(uint id, PixelBufferScope scope)
            {
                Memory<byte> pixels = scope.Allocate(3 * 2 * 4);
                for (int offset = 0; offset < pixels.Length; offset += 4)
                {
                    ReadOnlySpan<byte> color = id switch
                    {
                        2 => [255, 0, 0, 128],
                        3 => [0, 0, 255, 128],
                        _ => throw new ArgumentOutOfRangeException(nameof(id))
                    };
                    color.CopyTo(pixels.Span.Slice(offset, 4));
                }
                return Task.FromResult(new RgbaImage(3, 2, pixels, new ImageColorInformation(1, 13, ReadOnlyMemory<byte>.Empty)));
            }
        }
    }
}

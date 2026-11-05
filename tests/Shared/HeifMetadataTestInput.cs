// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Mediana.Core.Metadata;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Images.Heif;
using Mediana.Images.Heif.Container;

namespace Mediana.Tests
{
    internal static class HeifMetadataTestInput
    {
        internal static async Task<byte[]> CreateAsync(string root, uint width, uint height, string name = "basic-8bit.heic")
        {
            byte[] input = await File.ReadAllBytesAsync(Path.Combine(root, name));
            HeifContainer container = new HeifContainerReader(HeicDecodeOptions.Default).Read(input);
            HeifItem primary = container.Items[container.PrimaryItemId];
            HeifBox extent = primary.Associations.Select(association => container.Properties[association.Index])
                .Single(property => property.Type == HeifBox.Code("ispe"));
            BinaryPrimitives.WriteUInt32BigEndian(input.AsSpan(extent.Offset + 4), width);
            BinaryPrimitives.WriteUInt32BigEndian(input.AsSpan(extent.Offset + 8), height);
            ReadOnlyMemory<byte> coded = container.ReadData(container.PrimaryItemId);
            if (!MemoryMarshal.TryGetArray(coded, out ArraySegment<byte> segment) || segment.Array != input)
            {
                throw new InvalidDataException("The primary coded item must reference the authored input buffer.");
            }
            input.AsSpan(segment.Offset, segment.Count).Clear();
            return input;
        }

        internal static async Task VerifyAsync(string root)
        {
            HeicImageDecoder decoder = new(SampleAspectRatio.Square, new(maxPixels: 1));
            foreach ((uint width, uint height, string name) in new[]
            {
                (16320u, 12240u, "basic-8bit.heic"),
                (16320u, 12240u, "rotate-90.heic"),
                ((uint)int.MaxValue, (uint)int.MaxValue, "basic-8bit.heic")
            })
            {
                byte[] input = await CreateAsync(root, width, height, name);
                byte[] unchanged = input.ToArray();
                ImageInfo info = await decoder.ReadInfoAsync(input);
                double expectedWidth = name == "rotate-90.heic" ? height : width;
                double expectedHeight = name == "rotate-90.heic" ? width : height;
                if (info.FormatId != "heif" || info.Width != expectedWidth || info.Height != expectedHeight
                    || info.DimensionUnit != ImageDimensionUnit.Pixels || !input.SequenceEqual(unchanged))
                {
                    throw new InvalidDataException("HEIF metadata must expose transformed dimensions without interpreting coded pixels or changing input bytes.");
                }
                MediaBufferAllocator allocator = new(4);
                try
                {
                    using VideoFrame frame = await decoder.DecodeAsync(input, new(), allocator);
                    throw new InvalidOperationException("Pixel decode must enforce its configured admission limit.");
                }
                catch (InvalidDataException error) when (error.Message.Contains("pixel limit", StringComparison.Ordinal))
                {
                    if (allocator.AllocatedBytes != 0)
                    {
                        throw new InvalidOperationException("Oversized HEIF input must fail before output allocation.", error);
                    }
                }
            }
        }
    }
}

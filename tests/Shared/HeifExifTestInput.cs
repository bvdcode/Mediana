// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Images.Heif.Binary;
using Mediana.Images.Heif.Container;
using Mediana.Images.Heif;
using Mediana.Core.Metadata;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    internal static class HeifExifTestInput
    {
        internal static async Task VerifyAsync(string root)
        {
            await VerifyDeclarationsAsync(root);
            byte[] input = await CreateAsync(root, false);
            ImageInfo info = await new HeicImageDecoder(SampleAspectRatio.Square, new(maxPixels: 1)).ReadInfoAsync(input);
            Array.Clear(input);
            Stack<MediaMetadata> pending = new();
            pending.Push(info.Metadata);
            bool found = false;
            while (pending.TryPop(out MediaMetadata? metadata))
            {
                foreach (MediaProperty property in metadata.Properties)
                {
                    if (property.Namespace == "exif" && property.Name == "Make")
                    {
                        if (property.Values[0].AsText() != "Камера") { throw new InvalidDataException("HEIF Exif text changed after inspection."); }
                        found = true;
                    }
                }
                foreach (MediaMetadata child in metadata.Children) { pending.Push(child); }
            }
            if (!found) { throw new InvalidDataException("HEIF inspection did not expose the associated Exif metadata."); }
        }

        private static async Task VerifyDeclarationsAsync(string root)
        {
            HeicImageDecoder decoder = new(SampleAspectRatio.Square, new(maxPixels: 1));
            foreach ((string name, int depth, int chroma, int count) in new[]
            {
                ("basic-8bit.heic", 8, 1, 1), ("basic-10bit.heic", 10, 1, 1),
                ("monochrome.heic", 8, 0, 1), ("grid.heic", 8, 1, 4)
            })
            {
                byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(root, name));
                ImageInfo info = await decoder.ReadInfoAsync(encoded);
                MediaMetadata encoding = info.Metadata.Children.Single(child => child.Scope == "encoding/0");
                long Integer(string space, string key) => encoding.Properties.Single(property => property.Namespace == space && property.Name == key).Values[0].AsInteger();
                if (Integer("hevc", "profile_idc") != 4 || Integer("hevc", "level_idc") != 30
                    || Integer("hevc", "luma_bit_depth") != depth || Integer("hevc", "chroma_format_idc") != chroma
                    || Integer("heif", "item_count") != count)
                {
                    throw new InvalidDataException("HEIF inspection changed the declared codec configuration or coded item count.");
                }
                if (name == "grid.heic" && (!info.Metadata.Properties.Any(property => property.Name == "grid_rows" && property.Values[0].AsInteger() == 2)
                    || !info.Metadata.Properties.Any(property => property.Name == "grid_columns" && property.Values[0].AsInteger() == 2)))
                {
                    throw new InvalidDataException("HEIF inspection lost the declared grid layout.");
                }
            }
            byte[] unknown = await File.ReadAllBytesAsync(Path.Combine(root, "basic-8bit.heic"));
            int header = unknown.AsSpan().IndexOf("hvcC"u8) + 4;
            unknown[header + 1] = 31;
            unknown[header + 12] = 255;
            ImageInfo declared = await decoder.ReadInfoAsync(unknown);
            MediaMetadata configuration = declared.Metadata.Children.Single(child => child.Scope == "encoding/0");
            if (configuration.Properties.Single(property => property.Name == "profile_idc").Values[0].AsInteger() != 31
                || configuration.Properties.Single(property => property.Name == "level_idc").Values[0].AsInteger() != 255)
            {
                throw new InvalidDataException("Inspection must retain unknown native profile and level declarations.");
            }
        }

        internal static async Task<byte[]> CreateAsync(string root, bool little)
        {
            byte[] input = await File.ReadAllBytesAsync(Path.Combine(root, "basic-8bit.heic"));
            List<HeifBox> top = Boxes(input);
            HeifBox metadata = top.Single(box => box.Type == HeifBox.Code("meta"));
            List<HeifBox> children = Boxes(metadata.Payload[4..]);
            int dataLength = children.Single(box => box.Type == HeifBox.Code("idat")).Payload.Length;
            byte[] exif = [0, 0, 0, 0, .. ExifMetadataTestInput.Create(little)];
            using MemoryStream output = new();
            foreach (HeifBox box in top)
            {
                if (box.Type != metadata.Type) { output.Write(Box(box.Type, box.Payload.Span)); continue; }
                using MemoryStream content = new();
                content.Write(new byte[4]);
                foreach (HeifBox child in children)
                {
                    byte[] payload = child.Payload.ToArray();
                    if (child.Type == HeifBox.Code("iinf"))
                    {
                        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(4), 2);
                        byte[] entry = Box(HeifBox.Code("infe"), [2, 0, 0, 0, 3, 232, 0, 0, .. "Exifmetadata\0"u8]);
                        payload = [.. payload, .. entry];
                    }
                    else if (child.Type == HeifBox.Code("iloc"))
                    {
                        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(6), 2);
                        byte[] location = [3, 232, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0];
                        BinaryPrimitives.WriteInt32BigEndian(location.AsSpan(8), dataLength);
                        BinaryPrimitives.WriteInt32BigEndian(location.AsSpan(12), exif.Length);
                        payload = [.. payload, .. location];
                    }
                    else if (child.Type == HeifBox.Code("iref"))
                    {
                        payload = [.. payload, .. Box(HeifBox.Code("cdsc"), [3, 232, 0, 1, 0, 1])];
                    }
                    else if (child.Type == HeifBox.Code("idat"))
                    {
                        payload = [.. payload, .. exif];
                    }
                    content.Write(Box(child.Type, payload));
                }
                output.Write(Box(box.Type, content.ToArray()));
            }
            return output.ToArray();
        }

        private static List<HeifBox> Boxes(ReadOnlyMemory<byte> data)
        {
            ByteReader reader = new(data);
            List<HeifBox> result = [];
            while (reader.Remaining != 0) { result.Add(HeifBox.Read(reader, 0)); }
            return result;
        }

        private static byte[] Box(uint type, ReadOnlySpan<byte> payload)
        {
            byte[] output = new byte[payload.Length + 8];
            BinaryPrimitives.WriteInt32BigEndian(output, output.Length);
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(4), type);
            payload.CopyTo(output.AsSpan(8));
            return output;
        }
    }
}

using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class Mp4MetadataTestInput
    {
        public static async Task VerifyAsync(string root)
        {
            using JsonDocument references = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "references.json")));
            foreach (JsonElement entry in references.RootElement.GetProperty("files").EnumerateArray())
            {
                string name = entry.GetProperty("file").GetString()!;
                byte[] sourceBytes = await File.ReadAllBytesAsync(Path.Combine(root, name));
                if (Convert.ToHexStringLower(SHA256.HashData(sourceBytes)) != entry.GetProperty("sha256").GetString())
                {
                    throw new InvalidDataException("The metadata fixture hash changed.");
                }
                MediaBufferAllocator allocator = new(1024 * 1024);
                await using FileStream file = File.OpenRead(Path.Combine(root, name));
                await using RandomAccessStreamByteSource source = new(file, leaveOpen: true);
                await source.ReadAsync(new byte[3]);
                await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
                JsonElement reference = entry.GetProperty("reference");
                JsonElement tags = reference.GetProperty("format").GetProperty("tags");
                (string Atom, string Tag)[] tagNames = [("©nam", "title"), ("©ART", "artist"), ("aART", "album_artist"),
                    ("©wrt", "composer"), ("©alb", "album"), ("©day", "date"), ("©cmt", "comment"), ("©gen", "genre"), ("©too", "encoder")];
                foreach ((string atom, string tag) in tagNames)
                {
                    string key = name == "covers.m4a" ? atom : tag;
                    Mp4MetadataItem value = reader.Metadata.Single(value => value.Name == key);
                    if (value.Text != tags.GetProperty(tag).GetString() || value.DataType != 1 || value.Locale != 0
                        || value.Namespace != (name == "covers.m4a" ? "mdir" : "mdta"))
                    {
                        throw new InvalidDataException($"MP4 metadata does not match independent tag '{tag}'.");
                    }
                }
                if (name == "mdta.mp4" && reader.Metadata.Single(value => value.Name == "custom.example").Text != "Value 🧪")
                {
                    throw new InvalidDataException("An indexed metadata key lost its Unicode value.");
                }
                long metadataBytes = allocator.AllocatedBytes;
                MediaBuffer? retained = null;
                byte[]? retainedReference = null;
                try
                {
                    int expectedCovers = name == "covers.m4a" ? 2 : 0;
                    if (reader.CoverArt.Count != expectedCovers)
                    {
                        throw new InvalidDataException("The cover count changed or became a timed video track.");
                    }
                    for (int index = 0; index < reader.CoverArt.Count; index++)
                    {
                        string suffix = index switch
                        {
                            0 => "png",
                            1 => "jpg",
                            _ => throw new InvalidDataException("Unexpected cover art.")
                        };
                        byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, "cover." + suffix));
                        Mp4CoverArtInfo info = reader.CoverArt[index];
                        using MediaBuffer cover = reader.ReadCoverArt(index);
                        using MediaBuffer raw = reader.ReadMetadataValue(info.MetadataItemIndex);
                        string contentType = index switch
                        {
                            0 => "image/png",
                            1 => "image/jpeg",
                            _ => throw new InvalidDataException("Unexpected cover type.")
                        };
                        if (!cover.Memory.Span.SequenceEqual(expected) || !raw.Memory.Span.SequenceEqual(expected)
                            || info.ByteLength != expected.Length || info.ContentType != contentType)
                        {
                            throw new InvalidDataException("The cover bytes differ from the independent extraction.");
                        }
                        retained?.Dispose();
                        retained = cover.Retain();
                        retainedReference = expected;
                    }
                    JsonElement[] packets = reference.GetProperty("packets").EnumerateArray()
                        .Where(packet => packet.GetProperty("stream_index").GetInt32() < reader.Streams.Count).ToArray();
                    Dictionary<int, Queue<JsonElement>> expectedPackets = packets.GroupBy(packet => packet.GetProperty("stream_index").GetInt32())
                        .ToDictionary(group => group.Key, group => new Queue<JsonElement>(group));
                    int packetIndex = 0;
                    MediaTimestamp? previousDecodeTime = null;
                    while (await reader.ReadPacketAsync() is MediaPacket packet)
                    {
                        using (packet)
                        {
                            if (!expectedPackets.TryGetValue(packet.StreamIndex, out Queue<JsonElement>? queue) || queue.Count == 0
                                || packet.DecodeTime is not MediaTimestamp decodeTime)
                            {
                                throw new InvalidDataException("The MP4 reader returned an unexpected packet or unknown decode time.");
                            }
                            JsonElement expected = queue.Dequeue();
                            packetIndex++;
                            if (packet.StreamIndex != expected.GetProperty("stream_index").GetInt32()
                                || packet.PresentationTime!.Value.Value != expected.GetProperty("pts").GetInt64()
                                || packet.DecodeTime!.Value.Value != expected.GetProperty("dts").GetInt64()
                                || "SHA256:" + Convert.ToHexStringLower(SHA256.HashData(packet.Data.Span)) != expected.GetProperty("data_hash").GetString())
                            {
                                throw new InvalidDataException($"Packet {packetIndex - 1} in {name}: "
                                    + $"actual stream/PTS/DTS/hash {packet.StreamIndex}/{packet.PresentationTime?.Value}/{packet.DecodeTime?.Value}/{Convert.ToHexStringLower(SHA256.HashData(packet.Data.Span))}; "
                                    + $"reference {expected.GetProperty("stream_index")}/{expected.GetProperty("pts")}/{expected.GetProperty("dts")}/{expected.GetProperty("data_hash")}.");
                            }
                            if (previousDecodeTime is MediaTimestamp previous && decodeTime.CompareTo(previous) < 0)
                            {
                                throw new InvalidDataException("The MP4 reader did not merge packets by exact decode time.");
                            }
                            previousDecodeTime = decodeTime;
                        }
                    }
                    if (packetIndex != packets.Length || expectedPackets.Values.Any(queue => queue.Count != 0)
                        || file.Position != 3 || allocator.AllocatedBytes != metadataBytes)
                    {
                        throw new InvalidOperationException("Metadata reading changed source ownership, position or buffer accounting.");
                    }
                    await reader.DisposeAsync();
                    if (retained is not null && (!retained.Memory.Span.SequenceEqual(retainedReference) || allocator.AllocatedBytes != metadataBytes))
                    {
                        throw new InvalidOperationException("An extracted cover did not survive reader disposal.");
                    }
                }
                finally
                {
                    retained?.Dispose();
                }
                if (allocator.AllocatedBytes != 0 || !file.CanRead)
                {
                    throw new InvalidOperationException("MP4 metadata retained buffers or closed the borrowed source.");
                }
            }
        }
    }
}

using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Containers.Flac;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class FlacMetadataTestInput
    {
        public static async Task VerifyAsync(string root, int packetBytes)
        {
            using JsonDocument document = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "references.json")));
            foreach (JsonElement fixture in document.RootElement.GetProperty("fixtures").EnumerateArray())
            {
                string path = Path.Combine(root, fixture.GetProperty("name").GetString() + ".flac");
                await using (FileStream file = File.OpenRead(path))
                {
                    if (Convert.ToHexStringLower(await SHA256.HashDataAsync(file)) != fixture.GetProperty("inputSha256").GetString())
                    {
                        throw new InvalidDataException("The real FLAC fixture hash changed.");
                    }
                }
                MediaBufferAllocator allocator = new(128 * 1024);
                MediaPacket? retainedPacket = null;
                List<MediaBuffer> covers = [];
                try
                {
                    await using StreamByteSource source = new(File.OpenRead(path));
                    await using (FlacReader reader = await FlacReader.OpenAsync(source, allocator, new FlacReadOptions { PacketBytes = packetBytes }))
                    {
                        FlacConfiguration configuration = reader.Configuration;
                        int sampleRate = fixture.GetProperty("sampleRate").GetInt32();
                        long samples = fixture.GetProperty("samples").GetInt64();
                        if (configuration.SampleRate != sampleRate || configuration.ChannelCount != fixture.GetProperty("channels").GetInt32()
                            || configuration.BitsPerSample != fixture.GetProperty("bitsPerSample").GetInt32() || configuration.TotalSamples != samples
                            || reader.Duration != new MediaDuration(samples, new TimeBase(1, sampleRate))
                            || reader.AudioOffset != fixture.GetProperty("audioOffset").GetInt64()
                            || reader.Streams.Count != 1 || reader.Streams[0].Kind != MediaKind.Audio || reader.Streams[0].CodecId != FlacConfiguration.CodecId
                            || reader.Streams[0].Configuration.Length != FlacConfiguration.StreamInfoBytes
                            || reader.Streams[0].TimeBase != new TimeBase(1, sampleRate))
                        {
                            throw new InvalidDataException("FLAC STREAMINFO, stream configuration or duration differs from the independent probe.");
                        }
                        foreach (JsonProperty tag in fixture.GetProperty("tags").EnumerateObject())
                        {
                            if (!reader.Comments.Any(item => item.Name.Equals(tag.Name, StringComparison.OrdinalIgnoreCase) && item.Value == tag.Value.GetString()))
                            {
                                throw new InvalidDataException($"FLAC comment {tag.Name} differs from the independent probe.");
                            }
                        }
                        JsonElement expectedCovers = fixture.GetProperty("covers");
                        if (reader.Pictures.Count != expectedCovers.GetArrayLength())
                        {
                            throw new InvalidDataException("FLAC picture count differs from the independent probe.");
                        }
                        for (int index = 0; index < reader.Pictures.Count; index++)
                        {
                            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, expectedCovers[index].GetString()!));
                            MediaBuffer cover = reader.ReadPicture(index);
                            covers.Add(cover);
                            string expectedType = Path.GetExtension(expectedCovers[index].GetString()) switch
                            {
                                ".png" => "image/png",
                                ".jpg" => "image/jpeg",
                                _ => throw new InvalidDataException("The FLAC reference picture type is unsupported.")
                            };
                            if (!cover.Memory.Span.SequenceEqual(expected) || reader.Pictures[index].MediaType != expectedType
                                || reader.Pictures[index].Width != 24 || reader.Pictures[index].Height != 16)
                            {
                                throw new InvalidDataException("The FLAC cover bytes or declarations differ from the original picture.");
                            }
                        }
                        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                        long bytes = 0;
                        while (true)
                        {
                            using MediaPacket? packet = await reader.ReadPacketAsync();
                            if (packet is null)
                            {
                                break;
                            }
                            if (packet.Data.IsEmpty || packet.Data.Length > packetBytes || packet.StreamIndex != 0
                                || packet.PresentationTime is not null || packet.DecodeTime is not null || packet.Duration is not null || packet.IsKeyFrame is not null)
                            {
                                throw new InvalidDataException("FLAC compressed chunks unexpectedly carry frame boundaries or timestamps.");
                            }
                            hash.AppendData(packet.Data.Span);
                            bytes += packet.Data.Length;
                            retainedPacket?.Dispose();
                            retainedPacket = packet.Retain();
                        }
                        if (bytes != fixture.GetProperty("audioBytes").GetInt64()
                            || Convert.ToHexStringLower(hash.GetHashAndReset()) != fixture.GetProperty("audioSha256").GetString()
                            || await reader.ReadPacketAsync() is not null)
                        {
                            throw new InvalidDataException("FLAC compressed bytes differ from the independent packet extents.");
                        }
                    }
                    _ = await source.ReadAsync(new byte[1]);
                    for (int index = 0; index < covers.Count; index++)
                    {
                        byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, fixture.GetProperty("covers")[index].GetString()!));
                        if (!covers[index].Memory.Span.SequenceEqual(expected))
                        {
                            throw new InvalidDataException("The retained FLAC picture did not survive reader disposal.");
                        }
                    }
                    if (retainedPacket is null || retainedPacket.Data.IsEmpty)
                    {
                        throw new InvalidDataException("The retained FLAC compressed packet did not survive reader disposal.");
                    }
                }
                finally
                {
                    retainedPacket?.Dispose();
                    foreach (MediaBuffer cover in covers)
                    {
                        cover.Dispose();
                    }
                }
                if (allocator.AllocatedBytes != 0)
                {
                    throw new InvalidOperationException("The FLAC reader retained media storage after final lease disposal.");
                }
            }
        }
    }
}

using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Containers.Mp3;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class Mp3ReferenceTestInput
    {
        public static async Task VerifyAsync(string root)
        {
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "references.json")));
            foreach (JsonElement item in manifest.RootElement.GetProperty("fixtures").EnumerateArray())
            {
                string name = item.GetProperty("name").GetString()!;
                byte[] file = await File.ReadAllBytesAsync(Path.Combine(root, name + ".mp3"));
                CheckHash(file, item.GetProperty("input_sha256").GetString());
                using MemoryStream input = new(file);
                await using StreamByteSource source = new(input, leaveOpen: true);
                MediaBufferAllocator allocator = new(128 * 1024);
                MediaPacket? retained = null;
                List<MediaBuffer> covers = [];
                try
                {
                    await using (Mp3Reader reader = await Mp3Reader.OpenAsync(source, allocator))
                    {
                        int rate = item.GetProperty("sample_rate").GetInt32();
                        if (reader.FirstFrame.SampleRate != rate || reader.FirstFrame.ChannelCount != item.GetProperty("channels").GetInt32()
                            || reader.AudioOffset != item.GetProperty("audio_offset").GetInt64()
                            || (reader.VbrInfo is not null) != item.GetProperty("has_xing").GetBoolean()
                            || reader.Streams.Count != 1 || reader.Streams[0].TimeBase != new TimeBase(1, rate)
                            || reader.Streams[0].CodecId != "mp3" || reader.Streams[0].Configuration.Length != 4
                            || !reader.TextFrames.Any(frame => frame.FrameId == "TIT2" && frame.Values.SequenceEqual(new[] { "MP3 reference" }))
                            || !reader.TextFrames.Any(frame => frame.FrameId == "TPE1" && frame.Values.SequenceEqual(new[] { "Mediana" })))
                        {
                            throw new InvalidDataException($"MP3 stream metadata differs for {name}.");
                        }
                        JsonElement pictures = item.GetProperty("pictures");
                        if (reader.Pictures.Count != pictures.GetArrayLength())
                        {
                            throw new InvalidDataException("MP3 picture count differs.");
                        }
                        for (int index = 0; index < reader.Pictures.Count; index++)
                        {
                            Id3PictureInfo info = reader.Pictures[index];
                            JsonElement expected = pictures[index];
                            if (info.Description != expected.GetProperty("description").GetString()
                                || info.MediaType != expected.GetProperty("media_type").GetString()
                                || info.PictureType != expected.GetProperty("picture_type").GetInt32()
                                || info.ByteLength != expected.GetProperty("bytes").GetInt32())
                            {
                                throw new InvalidDataException("MP3 picture metadata differs.");
                            }
                            MediaBuffer picture = reader.ReadPicture(index);
                            covers.Add(picture);
                            CheckHash(picture.Memory.Span, expected.GetProperty("sha256").GetString());
                        }
                        JsonElement packets = item.GetProperty("packets");
                        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                        long samples = 0;
                        foreach (JsonElement expected in packets.EnumerateArray())
                        {
                            using MediaPacket packet = await reader.ReadPacketAsync() ?? throw new InvalidDataException("MP3 ended early.");
                            int length = expected.GetProperty("bytes").GetInt32();
                            long pts = expected.GetProperty("pts_samples").GetInt64();
                            long duration = expected.GetProperty("duration_samples").GetInt64();
                            if (packet.StreamIndex != 0 || packet.IsKeyFrame is not null || packet.Data.Length != length
                                || packet.PresentationTime != new MediaTimestamp(pts, new TimeBase(1, rate))
                                || packet.DecodeTime != packet.PresentationTime
                                || packet.Duration != new MediaDuration(duration, new TimeBase(1, rate))
                                || !packet.Data.Span.SequenceEqual(file.AsSpan(expected.GetProperty("position").GetInt32(), length)))
                            {
                                throw new InvalidDataException($"MP3 packet extent/bytes/timing differs for {name}.");
                            }
                            CheckHash(packet.Data.Span, expected.GetProperty("sha256").GetString());
                            hash.AppendData(packet.Data.Span);
                            samples += duration;
                            retained?.Dispose();
                            retained = packet.Retain();
                        }
                        if (await reader.ReadPacketAsync() is not null || await reader.ReadPacketAsync() is not null
                            || reader.CodedDuration != new MediaDuration(samples, new TimeBase(1, rate))
                            || reader.TrailingTag?.Title != "MP3 reference" || reader.TrailingTag.Track != 3
                            || Convert.ToHexStringLower(hash.GetHashAndReset()) != item.GetProperty("audio_sha256").GetString())
                        {
                            throw new InvalidDataException("MP3 final duration, trailer or complete compressed bytes differ.");
                        }
                    }
                    if (!input.CanRead || input.Position != input.Length || retained is null || retained.Data.IsEmpty)
                    {
                        throw new InvalidDataException("MP3 input ownership or retained output is invalid.");
                    }
                    for (int index = 0; index < covers.Count; index++)
                    {
                        CheckHash(covers[index].Memory.Span, item.GetProperty("pictures")[index].GetProperty("sha256").GetString());
                    }
                }
                finally
                {
                    retained?.Dispose();
                    foreach (MediaBuffer picture in covers)
                    {
                        picture.Dispose();
                    }
                }
                if (allocator.AllocatedBytes != 0)
                {
                    throw new InvalidOperationException("MP3 buffers were not released.");
                }
            }
        }

        private static void CheckHash(ReadOnlySpan<byte> data, string? expected)
        {
            if (Convert.ToHexStringLower(SHA256.HashData(data)) != expected)
            {
                throw new InvalidDataException("The MP3 reference bytes differ.");
            }
        }
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Formats.Audio;

namespace Mediana.Containers.Avi.Tests
{
    public class AviMp3Tests
    {
        [Fact]
        public async Task IndependentMp3PacketsPreservePayloadAndExactSampleClock()
        {
            byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "mjpeg-mp3.avi"));
            using JsonDocument reference = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Data", "mjpeg-mp3.json")));
            JsonElement packets = reference.RootElement.GetProperty("packets");
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using AviTestSource source = new(bytes, 3);
            await using (AviReader reader = await AviReader.OpenAsync(source, allocator))
            {
                Assert.Equal("mp3", reader.Streams[1].CodecId);
                Assert.Equal(new TimeBase(1, 48000), reader.Streams[1].TimeBase);
                Assert.Equal(packets.GetArrayLength(), reader.GetPacketCount(1));
                for (int index = packets.GetArrayLength() - 1; index >= 0; index--)
                {
                    using MediaPacket packet = await reader.ReadPacketAtAsync(1, index);
                    Assert.Equal(packets[index].GetProperty("data_hash").GetString()![7..], Convert.ToHexString(SHA256.HashData(packet.Data.Span)), ignoreCase: true);
                    Assert.Equal(new MediaTimestamp(index * 1152, new(1, 48000)), packet.PresentationTime);
                    Assert.Equal(new MediaDuration(1152, new(1, 48000)), packet.Duration);
                    Assert.Equal(packet.Data.Length, Mp3FrameHeader.Read(packet.Data.Span[..4]).FrameBytes);
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task Mp3HeadersAndPayloadCanSpanArbitraryAudioChunks()
        {
            byte[] original = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "mjpeg-mp3.avi"));
            using JsonDocument reference = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Data", "mjpeg-mp3.json")));
            using MemoryStream audio = new();
            foreach (JsonElement packet in reference.RootElement.GetProperty("packets").EnumerateArray())
            {
                int offset = int.Parse(packet.GetProperty("pos").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                int length = int.Parse(packet.GetProperty("size").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                audio.Write(original.AsSpan(offset, length));
            }
            byte[] compressed = audio.ToArray();
            foreach (int chunkSize in new[] { 1, 3, 277, 1024, compressed.Length })
            {
                byte[] input = BuildAudioAvi(compressed, chunkSize);
                await using AviTestSource source = new(input, 3);
                MediaBufferAllocator allocator = new(8 * 1024 * 1024);
                await using (AviReader reader = await AviReader.OpenAsync(source, allocator))
                {
                    int offset = 0;
                    long count = reader.GetPacketCount(0);
                    for (long index = 0; index < count; index++)
                    {
                        using MediaPacket packet = await reader.ReadPacketAtAsync(0, index);
                        Assert.True(compressed.AsSpan(offset, packet.Data.Length).SequenceEqual(packet.Data.Span));
                        offset += packet.Data.Length;
                    }
                    Assert.Equal(compressed.Length, offset);
                }
                Assert.Equal(0, allocator.AllocatedBytes);
            }
        }

        private static byte[] BuildAudioAvi(byte[] audio, int chunkSize)
        {
            byte[] main = new byte[56];
            Put(main, 24, 1);
            byte[] header = new byte[56];
            "auds"u8.CopyTo(header);
            Put(header, 20, 1); Put(header, 24, 12000); Put(header, 32, (uint)audio.Length); Put(header, 44, 1);
            byte[] format = new byte[18];
            BinaryPrimitives.WriteUInt16LittleEndian(format, 0x55);
            BinaryPrimitives.WriteUInt16LittleEndian(format.AsSpan(2), 1);
            Put(format, 4, 48000); Put(format, 8, 12000);
            BinaryPrimitives.WriteUInt16LittleEndian(format.AsSpan(12), 1);
            using MemoryStream movie = new();
            movie.Write("movi"u8);
            for (int index = 0; index < audio.Length; index += chunkSize)
            { movie.Write(Chunk("00wb", audio.AsSpan(index, Math.Min(chunkSize, audio.Length - index)).ToArray())); }
            return Chunk("RIFF", [.. "AVI "u8, .. Chunk("LIST", [.. "hdrl"u8, .. Chunk("avih", main),
                .. Chunk("LIST", [.. "strl"u8, .. Chunk("strh", header), .. Chunk("strf", format)])]), .. Chunk("LIST", movie.ToArray())]);
        }

        private static void Put(byte[] data, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);
        private static byte[] Chunk(string id, byte[] data)
        {
            byte[] result = new byte[8 + data.Length + (data.Length & 1)];
            Encoding.ASCII.GetBytes(id, result);
            Put(result, 4, (uint)data.Length);
            data.CopyTo(result, 8);
            return result;
        }
    }
}

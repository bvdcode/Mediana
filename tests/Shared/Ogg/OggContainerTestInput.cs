// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Containers;
using Mediana.Containers.Ogg;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class OggContainerTestInput
    {
        public static async Task VerifyAsync(string root)
        {
            foreach ((string name, int count, long duration) in new[] { ("opus-stereo", 69, 65760L), ("vorbis-stereo", 61, 60417L) })
            {
                MediaBufferAllocator allocator = new(1024 * 1024);
                using FileStream file = File.OpenRead(Path.Combine(root, name + ".ogg"));
                await using RandomAccessStreamByteSource source = new(file, leaveOpen: true);
                byte[] reference = await File.ReadAllBytesAsync(Path.Combine(root, name + ".packets"));
                await using (OggReader reader = await OggReader.OpenAsync(source, allocator))
                {
                    if (reader.Info.FormatId != "ogg" || reader.GetPacketCount(0) != count || reader.Streams.Count != 1 ||
                        reader.Info.DeclaredDuration!.Value.Value != duration || file.Position != 0 ||
                        reader.Info.StreamInfo![0].Tags!.All(tag => tag.Name != "title"))
                    {
                        throw new InvalidDataException("Ogg stream metadata, duration or source ownership differs from the reference.");
                    }
                    List<(int Offset, int Length)> ranges = [];
                    int offset = 0;
                    while (offset < reference.Length)
                    {
                        int length = BinaryPrimitives.ReadInt32LittleEndian(reference.AsSpan(offset));
                        ranges.Add((offset + 4, length));
                        offset = checked(offset + 4 + length);
                    }
                    for (int index = count - 1; index >= 0; index--)
                    {
                        using MediaPacket packet = await reader.ReadPacketAtAsync(0, index);
                        (int start, int length) = ranges[index];
                        if (!packet.Data.Span.SequenceEqual(reference.AsSpan(start, length)))
                        {
                            throw new InvalidDataException("An indexed Ogg packet differs from independent demuxing.");
                        }
                    }
                    ContainerSeekPoint seek = reader.FindSeekPoint(0, new(duration / 2, reader.Streams[0].TimeBase), count)!.Value;
                    ContainerSeekPoint preroll = reader.GetPrerollStart(0, seek.PacketIndex, 1);
                    if (preroll.PacketIndex >= seek.PacketIndex || file.Position != 0)
                    {
                        throw new InvalidDataException("Ogg indexed seeking did not preserve its source or preceding codec history.");
                    }
                    for (int index = 0; index < count; index++)
                    {
                        using MediaPacket packet = await reader.ReadPacketAsync()
                            ?? throw new InvalidDataException("Ogg sequential reading ended early.");
                        (int start, int length) = ranges[index];
                        if (!packet.Data.Span.SequenceEqual(reference.AsSpan(start, length)))
                        {
                            throw new InvalidDataException("Indexed requests moved the Ogg sequential cursor.");
                        }
                    }
                    if (await reader.ReadPacketAsync() is not null) { throw new InvalidDataException("Ogg reading exceeded its index."); }
                }
                if (allocator.AllocatedBytes != 0 || !file.CanRead || file.Position != 0)
                {
                    throw new InvalidOperationException("Ogg reading retained storage or changed its borrowed source.");
                }
            }
        }
    }
}

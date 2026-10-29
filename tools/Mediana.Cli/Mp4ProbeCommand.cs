// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Cli
{
    internal static class Mp4ProbeCommand
    {
        private const long BufferBudgetBytes = 64 * 1024 * 1024;

        public static async Task<int> RunAsync(string path)
        {
            MediaBufferAllocator allocator = new(BufferBudgetBytes);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(path));
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            await Console.Out.WriteLineAsync($"MP4/MOV: {reader.Tracks.Count} tracks; movie time base {reader.MovieTimeBase.Numerator}/{reader.MovieTimeBase.Denominator}; duration {reader.Duration?.Value}.");
            foreach (Mp4MetadataItem item in reader.Metadata)
            {
                await Console.Out.WriteLineAsync($"Metadata {item.Namespace}:{item.Name}; type {item.DataType}; locale {item.Locale}; {item.ByteLength} bytes; text {item.Text}.");
            }
            await Console.Out.WriteLineAsync($"Cover images: {reader.CoverArt.Count}.");
            List<PacketDigest> digests = [];
            try
            {
                foreach (Mp4TrackInfo track in reader.Tracks)
                {
                    digests.Add(new PacketDigest());
                    await Console.Out.WriteLineAsync($"Stream {track.Stream.StreamIndex}: {track.Stream.Kind}; {track.Stream.CodecId}/{track.SampleEntry}; samples {track.SampleCount}; time base {track.Stream.TimeBase.Numerator}/{track.Stream.TimeBase.Denominator}; media duration {track.MediaDuration?.Value}; edits {track.Edits.Count}.");
                }
                while (await reader.ReadPacketAsync() is MediaPacket packet)
                {
                    using (packet)
                    {
                        digests[packet.StreamIndex].Append(packet.Data.Span);
                    }
                }
                for (int index = 0; index < digests.Count; index++)
                {
                    PacketDigest digest = digests[index];
                    if (digest.Count != reader.Tracks[index].SampleCount)
                    {
                        throw new InvalidDataException("The packet count does not match the track metadata.");
                    }
                    await Console.Out.WriteLineAsync($"Stream {index}: {digest.Count} packets; {digest.Bytes} bytes; SHA256 {digest.Complete()}.");
                }
            }
            finally
            {
                foreach (PacketDigest digest in digests)
                {
                    digest.Dispose();
                }
            }
            await reader.DisposeAsync();
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("MP4 reader buffers were not released.");
            }
            await Console.Out.WriteLineAsync($"MP4 packet checks passed. Live buffer bytes: {allocator.AllocatedBytes}.");
            return 0;
        }
    }
}

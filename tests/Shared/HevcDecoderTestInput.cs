// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Mediana.Codecs;
using Mediana.Codecs.Hevc;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class HevcDecoderTestInput
    {
        public static async Task<int> VerifyAsync(string root, string name)
        {
            using MemoryStream bytes = new();
            await using (GZipStream compressed = new(File.OpenRead(Path.Combine(root, name + ".yuv.gz")), CompressionMode.Decompress))
            { await compressed.CopyToAsync(bytes); }
            ReadOnlyMemory<byte> expected = bytes.ToArray();
            MediaBufferAllocator allocator = new(64 * 1024 * 1024);
            int frames = 0;
            VideoFrame? retained = null;
            try
            {
                await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, name + ".mp4")));
                await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
                using IMediaCodec<MediaPacket, VideoFrame> decoder = new HevcDecoderFactory().Create(reader.Streams[0], allocator);
                List<(MediaTimestamp? Timestamp, MediaDuration? Duration)> timing = [];
                int packets = checked((int)reader.GetPacketCount(0));
                for (int i = 0; i < packets; i++)
                {
                    using MediaPacket packet = await reader.ReadPacketAtAsync(0, i);
                    timing.Add((packet.PresentationTime, packet.Duration));
                }
                timing.Sort((left, right) => Nullable.Compare(left.Timestamp, right.Timestamp));
                for (int i = 0; i < packets; i++)
                {
                    using MediaPacket packet = await reader.ReadPacketAtAsync(0, i);
                    if (decoder.TrySend(packet) != CodecInputStatus.Accepted) { throw new InvalidDataException("HEVC did not accept a drained input packet."); }
                    Drain(false);
                }
                decoder.CompleteInput(); Drain(true);
                if (frames != packets) { throw new InvalidDataException($"HEVC {name}: {frames} output frames for {packets} input packets."); }
                decoder.Dispose(); await reader.DisposeAsync(); await source.DisposeAsync();
                if (retained is null) { throw new InvalidDataException("HEVC did not produce independently retained output."); }
                VerifyFrame(retained, expected.Span, 0, name);
                retained.Dispose(); retained = null;

                void Drain(bool completed)
                {
                    while (true)
                    {
                        CodecOutput<VideoFrame> output = decoder.Receive();
                        if (output.Status != CodecOutputStatus.Available)
                        {
                            CodecOutputStatus wanted = completed ? CodecOutputStatus.EndOfStream : CodecOutputStatus.InputRequired;
                            if (output.Status != wanted) { throw new InvalidDataException("HEVC output completion differs from the codec contract."); }
                            return;
                        }
                        using VideoFrame frame = output.Value;
                        VerifyFrame(frame, expected.Span, frames, name);
                        if (frame.Timestamp != timing[frames].Timestamp || frame.Duration != timing[frames].Duration)
                        { throw new InvalidDataException("HEVC output timing differs from the source packet."); }
                        if (frames == 0) { retained = frame.Retain(); }
                        frames++;
                    }
                }
            }
            finally { retained?.Dispose(); }
            if (allocator.AllocatedBytes != 0) { throw new InvalidDataException("HEVC public decoder retained storage after disposal."); }
            return frames;
        }

        public static int VerifyFrame(VideoFrame frame, ReadOnlySpan<byte> expected, int index, string name)
        {
            int sampleBytes = frame.Format.PixelFormat == VideoPixelFormat.Yuv420P10 ? 2 : 1;
            if (sampleBytes == 1 && frame.Format.PixelFormat != VideoPixelFormat.Yuv420P8)
            { throw new InvalidDataException("HEVC output has an unexpected pixel format."); }
            int size = frame.Planes.Sum(plane => plane.RowBytes * plane.Height), offset = checked(size * index);
            for (int plane = 0; plane < frame.Planes.Count; plane++)
            {
                for (int y = 0; y < frame.Planes[plane].Height; y++)
                {
                    ReadOnlySpan<byte> row = frame.GetRow(plane, y);
                    for (int x = 0; x < row.Length / sampleBytes; x++)
                    {
                        int actual = sampleBytes == 1 ? row[x] : MemoryMarshal.Cast<byte, ushort>(row)[x];
                        int wanted = sampleBytes == 1 ? expected[offset] : BinaryPrimitives.ReadUInt16LittleEndian(expected[offset..]);
                        if (actual != wanted)
                        { throw new InvalidDataException($"HEVC {name}, frame {index}, plane {plane}, sample {x},{y}: {actual}, expected {wanted}."); }
                        offset += sampleBytes;
                    }
                }
            }
            return size;
        }
    }
}

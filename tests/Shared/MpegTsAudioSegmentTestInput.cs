// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Mp4;
using Mediana.Containers.MpegTs;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class MpegTsAudioSegmentTestInput
    {
        public static async Task<byte[][]> VerifyAsync(string root, string file)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using MemoryStream first = new();
            using MemoryStream second = new();
            using MemoryStream third = new();
            MemoryStream[] outputs = [first, second, third];
            List<(int Stream, byte[] Data, long Pts, long Dts)> expected = [];
            List<(int Stream, byte[] Data, long Pts, long Dts)>[] windows = [[], [], []];
            MpegTsSegmentState?[] states = new MpegTsSegmentState?[3];
            IReadOnlyList<EncodedStreamInfo> streams;
            await using (RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, file))))
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator))
            {
                EncodedStreamInfo audio = reader.Streams.Single(stream => stream.Kind == MediaKind.Audio);
                streams = [audio];
                int count = checked((int)reader.GetPacketCount(audio.StreamIndex));
                int[] boundaries = [0, count / 3, count * 2 / 3, count];
                if (boundaries[1] == 0 || boundaries[2] <= boundaries[1])
                {
                    throw new InvalidDataException("The audio fixture must cover three nonempty packet windows.");
                }
                await using (MpegTsWriter writer = MpegTsWriter.Create(first, streams, allocator))
                {
                    for (int window = 0; window < outputs.Length; window++)
                    {
                        if (window != 0)
                        {
                            states[window] = writer.CaptureSegmentState();
                            await writer.BeginSegmentAsync(outputs[window]);
                        }
                        for (int sample = boundaries[window]; sample < boundaries[window + 1]; sample++)
                        {
                            using MediaPacket packet = await reader.ReadPacketAtAsync(audio.StreamIndex, sample);
                            (int Stream, byte[] Data, long Pts, long Dts) item = (packet.StreamIndex, packet.Data.ToArray(),
                                MpegTsTestInput.Ticks(packet.PresentationTime), MpegTsTestInput.Ticks(packet.DecodeTime));
                            expected.Add(item);
                            windows[window].Add(item);
                            await writer.WritePacketAsync(packet);
                        }
                    }
                    await writer.CompleteAsync();
                }
                foreach (int number in new[] { 2, 0, 1, 2 })
                {
                    using MemoryStream output = new();
                    await using (MpegTsWriter writer = number == 0 ? MpegTsWriter.Create(output, streams, allocator) :
                        MpegTsWriter.ResumeSegment(output, states[number]!, allocator))
                    {
                        for (int sample = boundaries[number]; sample < boundaries[number + 1]; sample++)
                        {
                            using MediaPacket packet = await reader.ReadPacketAtAsync(audio.StreamIndex, sample);
                            await writer.WritePacketAsync(packet);
                        }
                        await writer.CompleteAsync();
                    }
                    if (!output.ToArray().AsSpan().SequenceEqual(outputs[number].ToArray()))
                    {
                        throw new InvalidDataException("A resumed audio segment differs from continuous transport output.");
                    }
                }
            }
            if (allocator.AllocatedBytes != 0 || outputs.Any(output => !output.CanWrite))
            {
                throw new InvalidOperationException("Audio segments retained media buffers or closed a borrowed stream.");
            }
            byte[][] result = outputs.Select(output => output.ToArray()).ToArray();
            for (int number = 0; number < result.Length; number++)
            {
                ReadOnlySpan<byte> bytes = result[number];
                if (Pid(bytes) != 0 || Pid(bytes[188..]) != 0x1000)
                {
                    throw new InvalidDataException("Each audio segment must start with PAT and PMT.");
                }
                MpegTsTestInput.Verify(bytes, streams, windows[number]);
            }
            MpegTsTestInput.Verify(result.SelectMany(bytes => bytes).ToArray(), streams, expected);
            return result;
        }

        private static int Pid(ReadOnlySpan<byte> packet) => (packet[1] & 31) << 8 | packet[2];
    }
}

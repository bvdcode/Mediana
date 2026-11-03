// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using System.Numerics;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;
using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    internal static class AudioHlsTestInput
    {
        public static async Task VerifyAsync(string root, string file, string codecs, int frameSamples)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            List<MemoryStream> destinations = [];
            await using (RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, file))))
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator))
            {
                EncodedStreamInfo audio = reader.Streams.Single(stream => stream.Kind == MediaKind.Audio);
                await using AudioHlsWriter writer = AudioHlsWriter.Create(audio, (number, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    if (number != destinations.Count)
                    {
                        throw new InvalidDataException("Audio HLS segment numbers are not contiguous.");
                    }
                    MemoryStream destination = new();
                    destinations.Add(destination);
                    return ValueTask.FromResult<Stream>(destination);
                }, allocator, new() { TargetDuration = new(1, new(1, 4)) });
                List<(int Stream, byte[] Data, long Pts, long Dts)> all = [];
                List<List<(int Stream, byte[] Data, long Pts, long Dts)>> windows = [[]];
                List<long> starts = [];
                long? origin = null;
                long end = 0;
                int count = checked((int)reader.GetPacketCount(audio.StreamIndex));
                for (int sample = 0; sample < count; sample++)
                {
                    using MediaPacket packet = await reader.ReadPacketAtAsync(audio.StreamIndex, sample);
                    long start = packet.PresentationTime!.Value.Value;
                    if (!origin.HasValue)
                    {
                        origin = start;
                        starts.Add(start);
                    }
                    else if (((BigInteger)start - origin.Value) * audio.TimeBase.Numerator * 4 >=
                        (BigInteger)windows.Count * audio.TimeBase.Denominator)
                    {
                        windows.Add([]);
                        starts.Add(start);
                    }
                    (int Stream, byte[] Data, long Pts, long Dts) item = (packet.StreamIndex, packet.Data.ToArray(),
                        MpegTsTestInput.Ticks(packet.PresentationTime), MpegTsTestInput.Ticks(packet.DecodeTime));
                    all.Add(item);
                    windows[^1].Add(item);
                    if (audio.TimeBase.Numerator != 1)
                    {
                        throw new InvalidDataException("The audio fixture must use its sample clock.");
                    }
                    end = checked(start + frameSamples);
                    await writer.WritePacketAsync(packet);
                }
                await writer.CompleteAsync();
                await writer.CompleteAsync();
                await writer.DisposeAsync();
                if (!writer.IsComplete || writer.Codecs != codecs || destinations.Count != windows.Count ||
                    destinations.Any(destination => destination.CanWrite))
                {
                    throw new InvalidDataException("Audio HLS changed codec metadata, segment count or destination ownership.");
                }
                string playlist = writer.CreateVodPlaylist(number => $"audio-{number}.ts");
                string[] durationLines = playlist.Split('\n').Where(line => line.StartsWith("#EXTINF:", StringComparison.Ordinal)).ToArray();
                if (!playlist.StartsWith("#EXTM3U\n#EXT-X-VERSION:3\n#EXT-X-TARGETDURATION:1\n", StringComparison.Ordinal) ||
                    !playlist.EndsWith("#EXT-X-ENDLIST\n", StringComparison.Ordinal) ||
                    playlist.Contains("#EXT-X-INDEPENDENT-SEGMENTS", StringComparison.Ordinal) || durationLines.Length != windows.Count)
                {
                    throw new InvalidDataException("The audio VOD playlist has incorrect duration or restart declarations.");
                }
                long bytes = 0;
                for (int number = 0; number < destinations.Count; number++)
                {
                    byte[] data = destinations[number].ToArray();
                    MpegTsTestInput.Verify(data, [audio], windows[number]);
                    HlsMediaSegment segment = writer.Segments[number];
                    long length = (number + 1 < starts.Count ? starts[number + 1] : end) - starts[number];
                    if (segment.Number != number || segment.Start != new MediaTimestamp(starts[number], audio.TimeBase) ||
                        segment.Duration != new MediaDuration(length, audio.TimeBase) || segment.ByteLength != data.LongLength ||
                        !playlist.Contains($"\naudio-{number}.ts\n", StringComparison.Ordinal))
                    {
                        throw new InvalidDataException("Audio HLS differs from the independent exact packet-grid reference.");
                    }
                    decimal seconds = decimal.Parse(durationLines[number].AsSpan(8).TrimEnd(','), CultureInfo.InvariantCulture);
                    decimal expected = (decimal)length * audio.TimeBase.Numerator / audio.TimeBase.Denominator;
                    if (Math.Abs(seconds - expected) > 0.000000000000001m)
                    {
                        throw new InvalidDataException("A playlist duration differs from the complete packet coverage.");
                    }
                    bytes += data.LongLength;
                }
                if (writer.BytesWritten != bytes || writer.Segments.Sum(segment => segment.ByteLength) != bytes)
                {
                    throw new InvalidDataException("Audio HLS byte accounting omits transport overhead.");
                }
                MpegTsTestInput.Verify(destinations.SelectMany(destination => destination.ToArray()).ToArray(), [audio], all);
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("The audio HLS presentation retained media buffers.");
            }
        }
    }
}

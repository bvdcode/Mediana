// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class Mp4FragmentWriterTests
    {
        [Theory]
        [InlineData("avc-segments.mp4")]
        [InlineData("avc-cabac-b-1.mp4")]
        [InlineData("avc-tail.mov")]
        public async Task InitializationKeepsDescriptionsAndEditsButContainsNoSamples(string file)
        {
            byte[] bytes = await Mp4TestData.ReadAsync(file);
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(bytes, 29);
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator))
            {
                source.Reads.Clear();
                using MemoryStream output = new();
                await Mp4FragmentWriter.WriteInitializationAsync(reader, [0], output, allocator);
                Assert.Empty(source.Reads);
                ReadOnlyMemory<byte> init = output.ToArray();
                Assert.Equal(new[] { "ftyp", "moov" }, Boxes(init).Select(box => box.Type));
                Assert.Equal("iso6", Encoding.ASCII.GetString(Box(init, "ftyp").Span[..4]));
                ReadOnlyMemory<byte> movie = Box(init, "moov"), original = Box(bytes, "moov");
                Assert.Single(Boxes(movie), box => box.Type == "trak");
                Assert.Equal(0u, UInt32(Box(movie, "mvhd"), 16));
                ReadOnlyMemory<byte> track = Box(movie, "trak"), oldTrack = Box(original, "trak");
                Assert.Equal(reader.Tracks[0].TrackId, UInt32(Box(track, "tkhd"), 12));
                Assert.Equal(0u, UInt32(Box(track, "tkhd"), 20));
                Assert.Equal(Boxes(oldTrack).Where(box => box.Type == "edts").Select(box => box.Data.ToArray()),
                    Boxes(track).Where(box => box.Type == "edts").Select(box => box.Data.ToArray()));
                ReadOnlyMemory<byte> samples = Box(Box(Box(track, "mdia"), "minf"), "stbl");
                Assert.Equal(Box(Box(Box(Box(oldTrack, "mdia"), "minf"), "stbl"), "stsd").ToArray(), Box(samples, "stsd").ToArray());
                foreach (string type in new[] { "stts", "stsc", "stco" }) { Assert.Equal(new byte[8], Box(samples, type).ToArray()); }
                Assert.Equal(new byte[12], Box(samples, "stsz").ToArray());
                Assert.Equal(reader.Tracks[0].TrackId, UInt32(Box(Box(movie, "mvex"), "trex"), 4));
                Assert.True(output.CanWrite);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData("avc-segments.mp4", true)]
        [InlineData("avc-cabac-b-1.mp4", false)]
        [InlineData("avc-tail.mov", false)]
        public async Task FragmentPreservesEverySelectedPayloadAndItsNativeClockWithoutEarlierReads(string file, bool audio)
        {
            byte[] bytes = await Mp4TestData.ReadAsync(file);
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(bytes, 23);
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator))
            {
                int first = reader.Tracks[0].SampleCount / 2;
                List<Mp4SampleRange> ranges = [new(0, first, reader.Tracks[0].SampleCount - first)];
                if (audio) { ranges.Add(new(1, 264, 75)); }
                List<Mp4SampleInfo[]> expected = ranges.Select(range => Enumerable.Range(range.FirstSample, range.SampleCount)
                    .Select(sample => reader.GetSampleInfo(range.StreamIndex, sample)).ToArray()).ToList();
                source.Reads.Clear();
                long retained = allocator.AllocatedBytes;
                using MemoryStream output = new();
                await Mp4FragmentWriter.WriteFragmentAsync(reader, ranges, 43, output, allocator);
                Assert.Equal(retained, allocator.AllocatedBytes);
                Assert.Equal(expected.SelectMany(track => track).Sum(sample => sample.Size), source.Reads.Sum(read => read.Length));
                Assert.All(source.Reads, read => Assert.Contains(expected.SelectMany(track => track),
                    sample => read.Offset >= sample.Offset && read.Offset + read.Length <= sample.Offset + sample.Size));
                ReadOnlyMemory<byte> fragment = output.ToArray(), moof = Box(fragment, "moof");
                Assert.Equal(43u, UInt32(Box(moof, "mfhd"), 4));
                ReadOnlyMemory<byte>[] tracks = Boxes(moof).Where(box => box.Type == "traf").Select(box => box.Data).ToArray();
                Assert.Equal(ranges.Count, tracks.Length);
                int mdatStart = checked(8 + moof.Length + 8), payloadOffset = 0;
                for (int track = 0; track < tracks.Length; track++)
                {
                    ReadOnlyMemory<byte> traf = tracks[track], run = Box(traf, "trun");
                    Assert.Equal(0x020000u, UInt32(Box(traf, "tfhd"), 0));
                    Assert.Equal(reader.Tracks[ranges[track].StreamIndex].TrackId, UInt32(Box(traf, "tfhd"), 4));
                    Assert.Equal((ulong)expected[track][0].DecodeTime.Value, BinaryPrimitives.ReadUInt64BigEndian(Box(traf, "tfdt").Span[4..]));
                    Assert.Equal((uint)expected[track].Length, UInt32(run, 4));
                    Assert.Equal((uint)(mdatStart + payloadOffset), UInt32(run, 8));
                    bool signed = run.Span[0] == 1;
                    for (int sample = 0; sample < expected[track].Length; sample++)
                    {
                        Mp4SampleInfo info = expected[track][sample];
                        int at = 12 + sample * 16;
                        Assert.Equal((uint)info.Duration.Value, UInt32(run, at));
                        Assert.Equal((uint)info.Size, UInt32(run, at + 4));
                        Assert.Equal(info.IsSync ? 0x02000000u : 0x01010000u, UInt32(run, at + 8));
                        long composition = signed ? BinaryPrimitives.ReadInt32BigEndian(run.Span[(at + 12)..]) : UInt32(run, at + 12);
                        Assert.Equal(info.PresentationTime.Value - info.DecodeTime.Value, composition);
                        Assert.True(bytes.AsSpan((int)info.Offset, info.Size).SequenceEqual(fragment.Span.Slice(mdatStart + payloadOffset, info.Size)));
                        payloadOffset += info.Size;
                    }
                }
                Assert.Equal(payloadOffset, Box(fragment, "mdat").Length);
                using MemoryStream repeated = new();
                await Mp4FragmentWriter.WriteFragmentAsync(reader, ranges, 43, repeated, allocator);
                Assert.Equal(output.ToArray(), repeated.ToArray());
                using MediaPacket? originalFirst = await reader.ReadPacketAsync();
                Assert.Equal(0, originalFirst!.DecodeTime!.Value.Value);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task InvalidRangesOrInsufficientHeaderBudgetWriteNothing()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync("avc-segments.mp4"));
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            using MemoryStream output = new();
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Mp4FragmentWriter.WriteFragmentAsync(reader,
                [new(0, 35, 2)], 1, output, allocator).AsTask());
            await Assert.ThrowsAsync<ArgumentException>(() => Mp4FragmentWriter.WriteInitializationAsync(reader, [0, 0], output, allocator).AsTask());
            await Assert.ThrowsAsync<MediaMemoryLimitException>(() => Mp4FragmentWriter.WriteFragmentAsync(reader,
                [new(0, 0, 1)], 1, output, new(1)).AsTask());
            Assert.Empty(output.ToArray());
        }

        private static uint UInt32(ReadOnlyMemory<byte> data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data.Span[offset..]);

        private static ReadOnlyMemory<byte> Box(ReadOnlyMemory<byte> data, string type) => Boxes(data).First(box => box.Type == type).Data;

        private static List<(string Type, ReadOnlyMemory<byte> Data)> Boxes(ReadOnlyMemory<byte> data)
        {
            List<(string Type, ReadOnlyMemory<byte> Data)> boxes = [];
            int position = 0;
            while (position < data.Length)
            {
                int length = checked((int)UInt32(data, position));
                Assert.InRange(length, 8, data.Length - position);
                boxes.Add((Encoding.ASCII.GetString(data.Span.Slice(position + 4, 4)), data.Slice(position + 8, length - 8)));
                position += length;
            }
            return boxes;
        }
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1IntraPredictionTests
    {
        [Theory]
        [InlineData("smoothRegions", 160, 136, false, 3)]
        [InlineData("smoothMono", 117, 83, true, 1)]
        [InlineData("smoothOdd", 37, 29, false, 1)]
        public void PublicCodecMatchesIndependentSmoothAndPaethVideo(string name, int width, int height, bool monochrome, int count)
        {
            byte[] encoded = Read(name + ".obu");
            byte[] expected = Read(name + ".yuv");
            MediaBufferAllocator allocator = new(1024 * 1024);
            IReadOnlyList<ReadOnlyMemory<byte>> units = Av1SequencePictureTests.AccessUnits(encoded);
            int level;
            using (Av1PictureDecoder reader = new(allocator))
            using (Av1Picture first = reader.Decode(units[0].Span))
            {
                level = first.Sequence.Tools.Level;
            }
            EncodedStreamInfo stream = new(0, MediaKind.Video, "av1", StreamFormats.Av1, new(1, 25), new byte[] { 0x81, (byte)level, monochrome ? (byte)0x1C : (byte)0x0C, 0 });
            int source = 0;
            using (IMediaCodec<MediaPacket, VideoFrame> codec = new Av1DecoderFactory().Create(stream, allocator))
            {
                for (int index = 0; index < units.Count; index++)
                {
                    using WritableMediaBuffer write = allocator.Allocate(units[index].Length);
                    units[index].Span.CopyTo(write.Span);
                    using MediaBuffer buffer = write.Publish();
                    using MediaPacket packet = new(0, buffer, new(index, new(1, 25)), duration: new(1, new(1, 25)));
                    Assert.Equal(CodecInputStatus.Accepted, codec.TrySend(packet));
                    using VideoFrame frame = codec.Receive().Value;
                    Assert.Equal(width, frame.Format.Width);
                    Assert.Equal(height, frame.Format.Height);
                    for (int plane = 0; plane < frame.Planes.Count; plane++)
                    {
                        for (int row = 0; row < frame.Planes[plane].Height; row++)
                        {
                            ReadOnlySpan<byte> actual = frame.GetRow(plane, row);
                            Assert.True(expected.AsSpan(source, actual.Length).SequenceEqual(actual), $"Frame {index}, plane {plane}, row {row} differs.");
                            source += actual.Length;
                        }
                    }
                }
                codec.CompleteInput();
                Assert.Equal(CodecOutputStatus.EndOfStream, codec.Receive().Status);
            }
            Assert.Equal(count, units.Count);
            Assert.Equal(expected.Length, source);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void NativeSmoothAndPaethMatchAllTransformGeometries()
        {
            using FileStream file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "intra.bin.gz"));
            using GZipStream expanded = new(file, CompressionMode.Decompress);
            using BinaryReader reader = new(expanded);
            Assert.Equal("AV1INTR1"u8.ToArray(), reader.ReadBytes(8));
            Assert.Equal(608, reader.ReadInt32());
            for (int test = 0; test < 608; test++)
            {
                int width = reader.ReadByte();
                int height = reader.ReadByte();
                int mode = reader.ReadByte();
                byte[] above = reader.ReadBytes(width + 1);
                byte[] left = reader.ReadBytes(height + 1);
                byte[] expected = reader.ReadBytes(width * height);
                byte[] actual = new byte[width * height];
                Assert.Equal(width * height, expected.Length);
                Av1IntraPredictor.Predict(actual, width, width, height, width, height, above, left, mode);
                Assert.True(actual.AsSpan().SequenceEqual(expected), $"Intra prediction vector {test}, mode {mode}, {width}x{height} differs.");
                actual.AsSpan().Fill(237);
                int visibleWidth = width - 1;
                int visibleHeight = height - 1;
                Av1IntraPredictor.Predict(actual, width, width, height, visibleWidth, visibleHeight, above, left, mode);
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int index = y * width + x;
                        Assert.Equal(x < visibleWidth && y < visibleHeight ? expected[index] : (byte)237, actual[index]);
                    }
                }
            }
            Assert.Equal(-1, expanded.ReadByte());
        }

        private static byte[] Read(string file) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", file));
    }
}

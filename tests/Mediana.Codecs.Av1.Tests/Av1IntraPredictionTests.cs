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
        [InlineData("filterRegions", 160, 136, false, 3)]
        [InlineData("filterMono", 117, 83, true, 1)]
        [InlineData("filterOdd", 37, 29, false, 1)]
        [InlineData("edgeRegions", 160, 136, false, 3)]
        [InlineData("edgeOdd", 37, 29, false, 1)]
        [InlineData("defaultStill", 160, 136, false, 1)]
        [InlineData("defaultMono", 117, 83, true, 1)]
        [InlineData("temporalTx8", 48, 40, false, 10)]
        [InlineData("temporalTx16", 48, 40, false, 10)]
        [InlineData("temporalFree", 48, 40, false, 10)]
        [InlineData("compoundAverage", 48, 40, false, 10)]
        [InlineData("compoundDistance", 48, 40, false, 10)]
        [InlineData("compoundFree", 48, 40, false, 10)]
        [InlineData("compoundBlend", 64, 48, false, 10)]
        [InlineData("compoundLossless", 64, 48, false, 10)]
        [InlineData("compoundLookahead", 64, 48, false, 10)]
        [InlineData("masked8", 96, 80, false, 10)]
        [InlineData("masked16", 96, 80, false, 10)]
        [InlineData("masked32", 96, 80, false, 10)]
        [InlineData("maskedFree", 96, 80, false, 10)]
        [InlineData("maskedOdd", 57, 43, false, 10)]
        [InlineData("maskedLarge", 128, 96, false, 10)]
        [InlineData("deltaQRegions", 160, 136, false, 3)]
        [InlineData("deltaLfRegions", 160, 136, false, 3)]
        [InlineData("deltaLfMono", 117, 83, true, 3)]
        [InlineData("defaultKey160", 160, 136, false, 1)]
        [InlineData("chromaPaletteOdd", 97, 81, false, 3)]
        [InlineData("chromaPaletteRegions", 96, 80, false, 3)]
        [InlineData("obmcDefault", 160, 136, false, 12)]
        [InlineData("obmcRegions", 96, 80, false, 8)]
        [InlineData("obmcOdd", 97, 81, false, 8)]
        [InlineData("warpDefault", 160, 136, false, 12)]
        [InlineData("warpRegions", 96, 80, false, 8)]
        [InlineData("warpOdd", 97, 81, false, 8)]
        public void PublicCodecMatchesIndependentDecodedVideo(string name, int width, int height, bool monochrome, int count)
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

        [Theory]
        [InlineData("intra.bin.gz", "AV1INTR1", 608, false)]
        [InlineData("filter-intra.bin.gz", "AV1FILT1", 1120, true)]
        public void NativePredictionMatchesAllTransformGeometries(string fileName, string magic, int count, bool recursive)
        {
            using FileStream file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", fileName));
            using GZipStream expanded = new(file, CompressionMode.Decompress);
            using BinaryReader reader = new(expanded);
            Assert.Equal(System.Text.Encoding.ASCII.GetBytes(magic), reader.ReadBytes(8));
            Assert.Equal(count, reader.ReadInt32());
            for (int test = 0; test < count; test++)
            {
                int width = reader.ReadByte();
                int height = reader.ReadByte();
                int mode = reader.ReadByte();
                byte[] above = reader.ReadBytes(width + 1);
                byte[] left = reader.ReadBytes(height + 1);
                byte[] expected = reader.ReadBytes(width * height);
                byte[] actual = new byte[width * height];
                Assert.Equal(width * height, expected.Length);
                Predict(recursive, actual, width, height, width, height, above, left, mode);
                Assert.True(actual.AsSpan().SequenceEqual(expected), $"Intra prediction vector {test}, mode {mode}, {width}x{height} differs.");
                actual.AsSpan().Fill(237);
                int visibleWidth = width - 1;
                int visibleHeight = height - 1;
                Predict(recursive, actual, width, height, visibleWidth, visibleHeight, above, left, mode);
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

        private static void Predict(bool recursive, Span<byte> destination, int width, int height, int visibleWidth, int visibleHeight,
            ReadOnlySpan<byte> above, ReadOnlySpan<byte> left, int mode)
        {
            if (recursive) { Av1FilterIntraPrediction.Predict(destination, width, width, height, visibleWidth, visibleHeight, above, left, mode); }
            else { Av1IntraPredictor.Predict(destination, width, width, height, visibleWidth, visibleHeight, above, left, mode); }
        }

        private static byte[] Read(string file) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", file));
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using System.Runtime.CompilerServices;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1RestorationTests
    {
        [Theory]
        [InlineData("restorationRegions", 160, 136, false, 3, 2)]
        [InlineData("restorationMono", 117, 83, true, 1, 2)]
        [InlineData("restorationMonoNoCdef", 117, 83, true, 1, 2)]
        [InlineData("restorationOdd", 37, 29, false, 1, 2)]
        [InlineData("restorationWiener", 256, 192, false, 1, 1)]
        [InlineData("restorationWienerRamp", 256, 192, false, 1, 1)]
        [InlineData("restorationMixed", 512, 192, false, 1, 3)]
        public void PublicCodecMatchesIndependentFilteredVideo(string name, int width, int height, bool monochrome, int count, int type)
        {
            byte[] encoded = Read(name + ".obu");
            byte[] expected = Read(name + ".yuv");
            MediaBufferAllocator allocator = new(1024 * 1024);
            IReadOnlyList<ReadOnlyMemory<byte>> units = Av1SequencePictureTests.AccessUnits(encoded);
            int level;
            using (Av1PictureDecoder reader = new(allocator))
            using (Av1Picture first = reader.Decode(units[0].Span))
            {
                Assert.Equal((Av1RestorationType)type, first.Header.Restoration.Y);
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
        public void NativeWienerAndEverySelfGuidedSetMatchAllSamples()
        {
            using FileStream file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "restoration.bin.gz"));
            using GZipStream expanded = new(file, CompressionMode.Decompress);
            using BinaryReader reader = new(expanded);
            Assert.Equal("AV1LRES1"u8.ToArray(), reader.ReadBytes(8));
            Assert.Equal(2048, reader.ReadInt32());
            int changed = 0;
            int sets = 0;
            for (int test = 0; test < 2048; test++)
            {
                int kind = reader.ReadByte();
                int width = reader.ReadByte();
                int height = reader.ReadByte();
                int x = reader.ReadByte();
                int y = reader.ReadByte();
                Av1RestorationUnit unit = new()
                {
                    V0 = reader.ReadSByte(), V1 = reader.ReadSByte(), V2 = reader.ReadSByte(),
                    H0 = reader.ReadSByte(), H1 = reader.ReadSByte(), H2 = reader.ReadSByte(),
                    Set = reader.ReadByte(), X0 = reader.ReadSByte(), X1 = reader.ReadSByte()
                };
                byte[] pixels = reader.ReadBytes(256);
                byte[] expected = reader.ReadBytes(256);
                Assert.Equal(256, pixels.Length);
                Assert.Equal(256, expected.Length);
                byte[] scratch = new byte[(height + 6) * 16];
                for (int row = 0; row < height + 6; row++)
                {
                    int source = Math.Clamp(y + row - 3, 0, 15);
                    pixels.AsSpan(source * 16, 16).CopyTo(scratch.AsSpan(row * 16));
                }
                if (!pixels.AsSpan().SequenceEqual(expected)) { changed++; }
                Av1RestorationSource<byte> input = new(scratch, default, 16, 16, 16, y, 0, 64);
                switch (kind)
                {
                    case 0:
                        Av1RestorationWiener.Filter(input, pixels, 16, x, y, width, height, unit);
                        break;
                    case 1:
                        sets |= 1 << unit.Set;
                        Av1RestorationSelfGuided.Filter(input, pixels, 16, x, y, width, height, unit);
                        break;
                    default:
                        throw new InvalidDataException("The restoration fixture kind is invalid.");
                }
                Assert.True(pixels.AsSpan().SequenceEqual(expected), $"Restoration vector {test}, kind {kind}, set {unit.Set} differs.");
            }
            Assert.Equal(65535, sets);
            Assert.True(changed > 1000);
            Assert.Equal(-1, expanded.ReadByte());
            Assert.Equal(10, Unsafe.SizeOf<Av1RestorationUnit>());
        }
        private static byte[] Read(string file) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", file));
    }
}

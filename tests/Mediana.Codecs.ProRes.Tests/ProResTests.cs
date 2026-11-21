// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Containers.Mp4;
using Mediana.Core.Video;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Codecs.ProRes.Tests
{
    public class ProResTests
    {
        [Theory]
        [InlineData("hq")]
        [InlineData("proxy")]
        [InlineData("lt")]
        [InlineData("standard")]
        [InlineData("field")]
        [InlineData("4444")]
        [InlineData("xq")]
        public async Task CompletePicturesMatchIndependentDecoder(string name)
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data");
            byte[] reference = await File.ReadAllBytesAsync(Path.Combine(root, name + ".yuv"));
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory()).AddVideoDecoder(new ProResDecoderFactory(new() { RecoverLegacyAlpha = true })).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, name + ".mov")));
            await using VideoSession session = await engine.OpenVideoAsync(source);
            List<(MediaTimestamp Time, byte[] Pixels)> decoded = [];
            int position = 0;
            while (await session.ReadFrameAsync() is VideoFrame frame)
            {
                using (frame)
                {
                    Assert.Equal(new MediaTimestamp(decoded.Count, new(1, 12)), frame.Timestamp);
                    decoded.Add((frame.Timestamp!.Value, frame.Data.ToArray()));
                    for (int plane = 0; plane < frame.Planes.Count; plane++)
                    {
                        for (int row = 0; row < frame.Planes[plane].Height; row++)
                        {
                            ReadOnlySpan<ushort> actual = MemoryMarshal.Cast<byte, ushort>(frame.GetRow(plane, row));
                            ReadOnlySpan<ushort> expected = MemoryMarshal.Cast<byte, ushort>(reference.AsSpan(position, actual.Length * 2));
                            for (int x = 0; x < actual.Length; x++)
                            {
                                int normalized = plane == 3 ? actual[x] >> 4 : Math.Clamp((int)actual[x], 4, (1 << ((PlanarVideoPixelFormat)frame.Format.PixelFormat).BitDepth) - 5);
                                Assert.True(Math.Abs(normalized - expected[x]) <= (plane == 3 ? 0 : 2), $"Plane {plane} row {row} column {x}: {actual[x]} versus {expected[x]}");
                            }
                            position += actual.Length * 2;
                        }
                    }
                }
            }
            Assert.Equal(reference.Length, position);
            Assert.True(session.CanSeek);
            foreach ((MediaTimestamp time, byte[] pixels) in decoded.AsEnumerable().Reverse())
            {
                await session.SeekAsync(time);
                using VideoFrame frame = await session.ReadFrameAsync() ?? throw new InvalidDataException("Missing ProRes seek frame.");
                Assert.Equal(time, frame.Timestamp);
                Assert.Equal(pixels, frame.Data.ToArray());
            }
            await session.DisposeAsync();
            Assert.Equal(0, session.AllocatedBufferBytes);
        }
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Codecs.ProRes;
using Mediana.Containers.Mp4;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;

namespace Mediana.NativeTests
{
    internal static class NativeProResTests
    {
        public static async Task RunAsync()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data", "ProRes");
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory())
                .AddVideoDecoder(new ProResDecoderFactory(new() { RecoverLegacyAlpha = true })).Build();
            foreach (string name in new[] { "proxy", "lt", "standard", "hq", "field", "4444", "xq" })
            {
                byte[] reference = await File.ReadAllBytesAsync(Path.Combine(root, name + ".yuv"));
                await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, name + ".mov")));
                await using VideoSession session = await engine.OpenVideoAsync(source);
                int offset = 0, count = 0;
                while (await session.ReadFrameAsync() is VideoFrame frame)
                {
                    using (frame)
                    {
                        Verify(frame, reference, ref offset);
                        if (frame.Timestamp != new MediaTimestamp(count++, new(1, 12)))
                        {
                            throw new InvalidDataException("ProRes frame timestamp differs from the MOV index.");
                        }
                    }
                }
                if (offset != reference.Length || count != 3)
                {
                    throw new InvalidDataException("ProRes sequence length differs from the reference.");
                }
                await session.SeekAsync(new(1, new(1, 12)));
                using (VideoFrame frame = await session.ReadFrameAsync() ?? throw new InvalidDataException("Missing ProRes seek output."))
                {
                    offset = reference.Length / count;
                    Verify(frame, reference, ref offset);
                }
                await session.DisposeAsync();
                if (session.AllocatedBufferBytes != 0)
                {
                    throw new InvalidDataException("ProRes retained frame storage after disposal.");
                }
            }
            Console.WriteLine("PASS ProRes: six profiles, interlacing, exact alpha, reference pictures and indexed seeks.");
        }

        private static void Verify(VideoFrame frame, byte[] reference, ref int offset)
        {
            int maximum = (1 << ((PlanarVideoPixelFormat)frame.Format.PixelFormat).BitDepth) - 5;
            for (int plane = 0; plane < frame.Planes.Count; plane++)
            {
                for (int row = 0; row < frame.Planes[plane].Height; row++)
                {
                    ReadOnlySpan<ushort> actual = MemoryMarshal.Cast<byte, ushort>(frame.GetRow(plane, row));
                    ReadOnlySpan<ushort> expected = MemoryMarshal.Cast<byte, ushort>(reference.AsSpan(offset, actual.Length * 2));
                    for (int x = 0; x < actual.Length; x++)
                    {
                        int normalized = plane == 3 ? actual[x] >> 4 : Math.Clamp((int)actual[x], 4, maximum);
                        if (Math.Abs(normalized - expected[x]) > (plane == 3 ? 0 : 2))
                        {
                            throw new InvalidDataException("ProRes samples differ from the independent decoder.");
                        }
                    }
                    offset += actual.Length * 2;
                }
            }
        }
    }
}

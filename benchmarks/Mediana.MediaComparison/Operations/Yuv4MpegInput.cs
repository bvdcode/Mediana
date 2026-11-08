// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using System.Text;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.OperationComparison
{
    internal class Yuv4MpegInput(Stream input, VideoFormat format, TimeBase clock)
    {
        private readonly byte[] marker = new byte[6];
        private readonly PlaneLayout[] layout = format.CreateTightLayout();
        private readonly int bytes = checked(format.Width * format.Height * 3 / 2);
        private long index;
        public VideoFormat Format { get; } = format;
        public TimeBase Clock { get; } = clock;

        public static async Task<Yuv4MpegInput> OpenAsync(Stream input)
        {
            byte[] header = new byte[4096];
            int count = 0;
            while (true)
            {
                if (count == header.Length) { throw new InvalidDataException("The YUV4MPEG2 header is too long."); }
                await input.ReadExactlyAsync(header.AsMemory(count, 1));
                if (header[count++] == '\n') { break; }
            }
            string[] fields = Encoding.ASCII.GetString(header, 0, count - 1).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields[0] != "YUV4MPEG2") { throw new InvalidDataException("Expected a YUV4MPEG2 input."); }
            int width = 0;
            int height = 0;
            TimeBase? clock = null;
            SampleAspectRatio aspect = new(1, 1);
            ChromaLocation? chroma = null;
            bool progressive = false;
            foreach (string field in fields.Skip(1))
            {
                switch (field[0])
                {
                    case 'W': width = Number(field[1..]); break;
                    case 'H': height = Number(field[1..]); break;
                    case 'F':
                        string[] rate = field[1..].Split(':');
                        clock = new(Number(rate[1]), Number(rate[0]));
                        break;
                    case 'A':
                        string[] ratio = field[1..].Split(':');
                        aspect = field == "A0:0" ? new(1, 1) : new(Number(ratio[0]), Number(ratio[1]));
                        break;
                    case 'I': progressive = field == "Ip"; break;
                    case 'C':
                        chroma = field switch
                        {
                            "C420" or "C420jpeg" => ChromaLocation.Center,
                            "C420mpeg2" => ChromaLocation.Left,
                            _ => throw new NotSupportedException("The comparison requires 8-bit planar 4:2:0 samples.")
                        };
                        break;
                    case 'X':
                        if (field is not ("XYSCSS=420JPEG" or "XYSCSS=420MPEG2" or "XCOLORRANGE=LIMITED"))
                        {
                            throw new NotSupportedException($"Unsupported YUV4MPEG2 extension: {field}.");
                        }
                        break;
                    default: throw new NotSupportedException($"Unsupported YUV4MPEG2 field: {field}.");
                }
            }
            if (!progressive || clock is null || chroma is null || (width & 1) != 0 || (height & 1) != 0)
            {
                throw new InvalidDataException("The comparison requires progressive even-sized YUV420 pictures with an explicit frame rate.");
            }
            VideoFormat format = new(width, height, VideoPixelFormat.Yuv420P8,
                new(ColorPrimaries.Bt709, TransferFunction.Bt709, MatrixCoefficients.Bt709, ColorRange.Limited, chroma.Value), aspect);
            return new(input, format, clock.Value);
        }

        public async Task<VideoFrame?> ReadFrameAsync(MediaBufferAllocator allocator)
        {
            int count = await input.ReadAsync(marker.AsMemory(0, 1));
            if (count == 0) { return null; }
            await input.ReadExactlyAsync(marker.AsMemory(1));
            if (!marker.AsSpan().SequenceEqual("FRAME\n"u8))
            {
                throw new InvalidDataException("The comparison requires untagged complete YUV4MPEG2 frames.");
            }
            using WritableMediaBuffer writable = allocator.Allocate(bytes);
            await input.ReadExactlyAsync(writable.Memory);
            using MediaBuffer data = writable.Publish();
            return new(Format, data, layout, new(index++, Clock), new(1, Clock));
        }

        private static int Number(string text) => int.Parse(text, CultureInfo.InvariantCulture);
    }
}

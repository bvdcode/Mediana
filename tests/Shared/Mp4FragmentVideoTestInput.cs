// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class Mp4FragmentVideoTestInput
    {
        public static async Task VerifyAsync(string file)
        {
            byte[] expected = await Mp4TestData.ReadAsync("avc-aac.sequence.yuv");
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory())
                .AddVideoDecoder(new H264DecoderFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Mp4TestData.PathFor(file)));
            await using VideoSession session = await engine.OpenVideoAsync(source, 0);
            List<MediaTimestamp> times = [];
            int count = 0;
            while (await session.ReadFrameAsync() is VideoFrame frame)
            {
                using (frame)
                {
                    int size = frame.Format.Width * frame.Format.Height * 3 / 2;
                    H264TestInput.VerifyFrame(frame, expected.AsSpan(count * size, size));
                    times.Add(frame.Timestamp!.Value);
                    count++;
                }
            }
            if (count != 8)
            {
                throw new InvalidDataException("The fragmented video frame count differs from the independent reference.");
            }
            for (int index = count - 1; index >= 0; index--)
            {
                await session.SeekAsync(times[index]);
                using VideoFrame frame = await session.ReadFrameAtOrAfterAsync(times[index])
                    ?? throw new InvalidDataException("The fragmented video seek returned no frame.");
                int size = frame.Format.Width * frame.Format.Height * 3 / 2;
                if (times[index] != frame.Timestamp)
                {
                    throw new InvalidDataException("The fragmented video seek changed the frame timestamp.");
                }
                H264TestInput.VerifyFrame(frame, expected.AsSpan(index * size, size));
            }
            await session.DisposeAsync();
            if (session.AllocatedBufferBytes != 0)
            {
                throw new InvalidOperationException("The fragmented video session retained media buffers.");
            }
        }
    }
}

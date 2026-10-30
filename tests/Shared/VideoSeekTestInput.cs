// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class VideoSeekTestInput
    {
        public static async Task VerifyAsync(string root, string name, int count, int width, int height, int frameRate)
        {
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, name + ".sequence.yuv"));
            int frameBytes = width * height * 3 / 2;
            if (expected.Length != count * frameBytes)
            {
                throw new InvalidDataException("The seek reference has an unexpected YUV length.");
            }
            MediaEngine engine = new MediaEngineBuilder()
                .AddContainer(new Mp4ReaderFactory(new Mp4ReadOptions { TimestampMode = Mp4TimestampMode.LinearMovie }))
                .AddVideoDecoder(new H264DecoderFactory()).Build();
            await using FileStream input = File.OpenRead(Path.Combine(root, name + ".mp4"));
            await using RandomAccessStreamByteSource source = new(input, leaveOpen: true);
            await using VideoSession session = await engine.OpenVideoAsync(source, 0);
            if (!session.CanSeek)
            {
                throw new InvalidOperationException("The indexed AVC session did not expose seeking.");
            }
            using VideoFrame retained = await session.ReadFrameAsync() ?? throw new InvalidDataException("The seek reference is empty.");
            foreach (int requested in new[] { count - 1, 1, count / 2, 0, count - 2, count, count + 1, 0 })
            {
                MediaTimestamp target = new(requested * 2L - 1, new TimeBase(1, frameRate * 2));
                await session.SeekAsync(target);
                using VideoFrame? frame = await session.ReadFrameAtOrAfterAsync(target);
                if (requested >= count)
                {
                    if (frame is not null)
                    {
                        throw new InvalidDataException("A seek beyond the video returned an unrelated frame.");
                    }
                    continue;
                }
                if (frame is null || frame.Timestamp is not MediaTimestamp timestamp ||
                    timestamp.CompareTo(new(requested, new TimeBase(1, frameRate))) != 0)
                {
                    throw new InvalidDataException("Seeking selected the wrong presentation timestamp.");
                }
                H264TestInput.VerifyFrame(frame, expected.AsSpan(requested * frameBytes, frameBytes));
                using VideoFrame? following = await session.ReadFrameAsync();
                if (requested + 1 < count)
                {
                    if (following is null)
                    {
                        throw new InvalidDataException("The seek session lost the following picture.");
                    }
                    H264TestInput.VerifyFrame(following, expected.AsSpan((requested + 1) * frameBytes, frameBytes));
                }
                else if (following is not null)
                {
                    throw new InvalidDataException("Seeking past the final picture produced extra output.");
                }
                H264TestInput.VerifyFrame(retained, expected.AsSpan(0, frameBytes));
            }
            await session.DisposeAsync();
            H264TestInput.VerifyFrame(retained, expected.AsSpan(0, frameBytes));
            retained.Dispose();
            if (session.AllocatedBufferBytes != 0 || !input.CanRead || input.Position != 0)
            {
                throw new InvalidOperationException("Seeking retained buffers, closed the borrowed stream or moved its cursor.");
            }
        }
    }
}

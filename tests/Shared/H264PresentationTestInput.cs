// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using System.IO.Compression;
using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class H264PresentationTestInput
    {
        public static async Task VerifyAsync(string path)
        {
            string[] rows = await File.ReadAllLinesAsync(path + ".timing.csv");
            await using FileStream compressed = File.OpenRead(path + ".yuv.gz");
            await using GZipStream decoded = new(compressed, CompressionMode.Decompress);
            using MemoryStream expected = new();
            await decoded.CopyToAsync(expected);
            byte[] pixels = expected.ToArray();
            int count = rows.Length - 1;
            int frameBytes = checked(96 * 96 * 3 / 2);
            if (count != 8 || pixels.Length != count * frameBytes)
            {
                throw new InvalidDataException("The presentation reference is incomplete.");
            }
            MediaEngine engine = new MediaEngineBuilder()
                .AddContainer(new Mp4ReaderFactory(new Mp4ReadOptions { TimestampMode = Mp4TimestampMode.LinearMovie }))
                .AddVideoDecoder(new H264DecoderFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(path + ".mp4"));
            await using VideoSession session = await engine.OpenVideoAsync(source, 0);
            for (int index = 0; index < count * 2; index++)
            {
                int referenceIndex = index < count ? index : count * 2 - index - 1;
                long[] values = rows[referenceIndex + 1].Split(',')
                    .Select(value => long.Parse(value, CultureInfo.InvariantCulture)).ToArray();
                TimeBase timeBase = new(checked((int)values[2]), checked((int)values[3]));
                MediaTimestamp timestamp = new(values[0], timeBase);
                if (index >= count)
                {
                    await session.SeekAsync(timestamp);
                }
                using VideoFrame frame = (index < count ? await session.ReadFrameAsync() : await session.ReadFrameAtOrAfterAsync(timestamp))
                    ?? throw new InvalidDataException("The presentation sequence ended early.");
                VideoFramePresentation presentation = values[4] switch
                {
                    0 => VideoFramePresentation.Frames(),
                    3 => VideoFramePresentation.Fields(VideoFieldParity.Top, 2),
                    4 => VideoFramePresentation.Fields(VideoFieldParity.Bottom, 2),
                    5 => VideoFramePresentation.Fields(VideoFieldParity.Top, 3),
                    6 => VideoFramePresentation.Fields(VideoFieldParity.Bottom, 3),
                    7 => VideoFramePresentation.Frames(2),
                    _ => throw new InvalidDataException("The presentation reference has an unexpected display structure.")
                };
                if (frame.Timestamp is not MediaTimestamp actual || actual.CompareTo(timestamp) != 0 ||
                    frame.Duration is not MediaDuration duration ||
                    new MediaTimestamp(duration.Value, duration.TimeBase).CompareTo(new(values[1], timeBase)) != 0 ||
                    frame.Presentation != presentation || frame.Format.ScanMode != VideoScanMode.Progressive)
                {
                    throw new InvalidDataException($"The frame presentation differs from the reference at index {referenceIndex}.");
                }
                H264TestInput.VerifyFrame(frame, pixels.AsSpan(referenceIndex * frameBytes, frameBytes));
                if (index == count - 1)
                {
                    using VideoFrame? extra = await session.ReadFrameAsync();
                    if (extra is not null)
                    {
                        throw new InvalidDataException("The decoder duplicated a repeated presentation picture.");
                    }
                }
            }
            await session.DisposeAsync();
            if (session.AllocatedBufferBytes != 0)
            {
                throw new InvalidOperationException("The presentation sequence retained media buffers.");
            }
        }
    }
}

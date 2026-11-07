// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using Mediana.Codecs.Hevc;
using Mediana.Containers.Matroska;
using Mediana.Containers.Mp4;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class HevcSeekTestInput
    {
        public static async Task VerifyAsync(string root, string name, int count, int rate, string extension = ".mp4")
        {
            using MemoryStream bytes = new();
            await using (GZipStream compressed = new(File.OpenRead(Path.Combine(root, name + ".yuv.gz")), CompressionMode.Decompress))
            { await compressed.CopyToAsync(bytes); }
            ReadOnlyMemory<byte> expected = bytes.ToArray();
            MediaEngine engine = new MediaEngineBuilder()
                .AddContainer(new Mp4ReaderFactory(new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
                .AddContainer(new MatroskaReaderFactory()).AddVideoDecoder(new HevcDecoderFactory()).Build();
            await using FileStream input = File.OpenRead(Path.Combine(root, name + extension));
            await using RandomAccessStreamByteSource source = new(input, leaveOpen: true);
            await using VideoSession session = await engine.OpenVideoAsync(source);
            if (!session.CanSeek) { throw new InvalidDataException("The indexed HEVC session did not expose seeking."); }
            using VideoFrame retained = await session.ReadFrameAsync() ?? throw new InvalidDataException("HEVC seek input is empty.");
            foreach (int index in new[] { count - 1, 1, count / 2, 0, count - 2, count, count + 1, 0 })
            {
                MediaTimestamp target = new(index * 2L - 1, new(1, rate * 2));
                await session.SeekAsync(target);
                using VideoFrame? frame = await session.ReadFrameAtOrAfterAsync(target);
                if (index >= count)
                {
                    if (frame is not null) { throw new InvalidDataException("HEVC seek beyond the input produced an unrelated picture."); }
                    continue;
                }
                if (frame is null) { throw new InvalidDataException($"HEVC {name} seek lost frame {index}."); }
                MediaTimestamp wanted = new(index, new(1, rate));
                MediaTimestamp actual = frame.Timestamp ?? throw new InvalidDataException("HEVC seek returned an untimed picture.");
                // Matroska timestamps may be quantized to the container's millisecond clock.
                if (Math.Abs(actual.Rescale(new(1, 1000), TimestampRounding.NearestEven).Value - wanted.Rescale(new(1, 1000), TimestampRounding.NearestEven).Value) > 1)
                { throw new InvalidDataException("HEVC seek selected an incorrect display timestamp."); }
                HevcDecoderTestInput.VerifyFrame(frame, expected.Span, index, name);
                using VideoFrame? following = await session.ReadFrameAsync();
                if (index + 1 < count)
                {
                    if (following is null) { throw new InvalidDataException("HEVC seek lost the following picture."); }
                    HevcDecoderTestInput.VerifyFrame(following, expected.Span, index + 1, name);
                }
                else if (following is not null) { throw new InvalidDataException("HEVC seek produced an extra final picture."); }
                HevcDecoderTestInput.VerifyFrame(retained, expected.Span, 0, name);
            }
            await session.DisposeAsync();
            HevcDecoderTestInput.VerifyFrame(retained, expected.Span, 0, name); retained.Dispose();
            if (session.AllocatedBufferBytes != 0 || !input.CanRead || input.Position != 0)
            { throw new InvalidDataException("HEVC seek retained storage or changed its borrowed stream."); }
        }
    }
}

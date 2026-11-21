// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Alac;
using Mediana.Containers.Mp4;
using Mediana.Core.Audio;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.NativeTests
{
    internal static class NativeAlacTests
    {
        public static async Task RunAsync()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data", "Alac");
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory())
                .AddAudioDecoder(new AlacDecoderFactory()).Build();
            foreach (string name in new[] { "mono16", "stereo16", "stereo24", "noise24", "silence24", "surround24" })
            {
                byte[] reference = await File.ReadAllBytesAsync(Path.Combine(root, name + ".pcm"));
                await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, name + ".m4a")));
                await using AudioSession session = await engine.OpenAudioAsync(source);
                List<(MediaTimestamp Time, int Offset, int Length)> frames = [];
                int offset = 0;
                while (await session.ReadFrameAsync() is AudioFrame frame)
                {
                    using (frame)
                    {
                        int length = frame.GetPlane(0).Length;
                        Verify(frame, reference.AsSpan(offset, length));
                        frames.Add((frame.Timestamp!.Value, offset, length));
                        offset += length;
                    }
                }
                if (offset != reference.Length)
                {
                    throw new InvalidDataException("ALAC output length differs from the independent reference.");
                }
                foreach ((MediaTimestamp time, int start, int length) in frames.AsEnumerable().Reverse())
                {
                    await session.SeekAsync(time);
                    using AudioFrame frame = await session.ReadFrameAsync() ?? throw new InvalidDataException("Missing ALAC seek output.");
                    Verify(frame, reference.AsSpan(start, length));
                    if (frame.Timestamp != time)
                    {
                        throw new InvalidDataException("ALAC seek timing differs from the source.");
                    }
                }
                await session.DisposeAsync();
                if (session.AllocatedBufferBytes != 0)
                {
                    throw new InvalidDataException("ALAC retained allocator storage after disposal.");
                }
            }
            Console.WriteLine("PASS ALAC: complete PCM, six-channel coded order, reverse seeks and released storage.");
        }

        private static void Verify(AudioFrame frame, ReadOnlySpan<byte> reference)
        {
            if (!frame.GetPlane(0).Span.SequenceEqual(reference))
            {
                throw new InvalidDataException("ALAC PCM differs from the independent reference.");
            }
        }
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Codecs.Dts;
using Mediana.Containers.Matroska;
using Mediana.Core.Audio;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.NativeTests
{
    internal static class NativeDtsTests
    {
        public static async Task RunAsync()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data", "Dts");
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new MatroskaReaderFactory())
                .AddAudioDecoder(new DtsDecoderFactory()).Build();
            foreach (string name in new[] { "stereo", "surround", "mono44100", "noise", "vectors", "predictors" })
            {
                byte[] reference = await File.ReadAllBytesAsync(Path.Combine(root, name + ".pcm"));
                await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, name + ".mkv")));
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
                    throw new InvalidDataException("DTS output length differs from the independent reference.");
                }
                foreach ((MediaTimestamp time, int start, int length) in frames.AsEnumerable().Reverse())
                {
                    await session.SeekAsync(time);
                    using AudioFrame frame = await session.ReadFrameAsync() ?? throw new InvalidDataException("Missing DTS seek output.");
                    Verify(frame, reference.AsSpan(start, length));
                    if (frame.Timestamp != time)
                    {
                        throw new InvalidDataException("DTS seek timing differs from the source.");
                    }
                }
                await session.DisposeAsync();
                if (session.AllocatedBufferBytes != 0)
                {
                    throw new InvalidDataException("DTS retained allocator storage after disposal.");
                }
            }
            await Mediana.Tests.DtsWaveExportTestInput.VerifyAsync(root);
            Console.WriteLine("PASS DTS: complete PCM, six-channel WAVE export, reverse seeks and released storage.");
        }

        private static void Verify(AudioFrame frame, ReadOnlySpan<byte> reference)
        {
            ReadOnlySpan<float> actual = MemoryMarshal.Cast<byte, float>(frame.GetPlane(0).Span);
            ReadOnlySpan<float> expected = MemoryMarshal.Cast<byte, float>(reference);
            for (int i = 0; i < actual.Length; i++)
            {
                if (Math.Abs(actual[i] - expected[i]) >= 0.00002)
                {
                    throw new InvalidDataException("DTS PCM differs from the independent reference.");
                }
            }
        }
    }
}

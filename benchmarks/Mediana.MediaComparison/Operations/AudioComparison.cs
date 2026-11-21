// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers;
using System.Diagnostics;
using System.Security.Cryptography;
using Mediana.Codecs.Alac;
using Mediana.Codecs.Dts;
using Mediana.Containers.Matroska;
using Mediana.Containers.Mp4;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.OperationComparison
{
    internal static class AudioComparison
    {
        public static async Task RunAsync(string input, string destination)
        {
            using Process process = Process.GetCurrentProcess();
            long allocated = GC.GetTotalAllocatedBytes(true);
            TimeSpan cpu = process.TotalProcessorTime;
            Stopwatch watch = Stopwatch.StartNew();
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory())
                .AddContainer(new MatroskaReaderFactory()).AddAudioDecoder(new AlacDecoderFactory())
                .AddAudioDecoder(new DtsDecoderFactory()).Build();
            MediaBufferAllocator allocator = new(long.MaxValue, MemoryPool<byte>.Shared);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(input));
            await using AudioSession session = await engine.OpenAudioAsync(source, allocator, new(), null);
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long samples = 0, bytes = 0;
            int frames = 0;
            while (await session.ReadFrameAsync() is AudioFrame frame)
            {
                using (frame)
                {
                    ReadOnlySpan<byte> pcm = frame.GetPlane(0).Span;
                    hash.AppendData(pcm);
                    samples += frame.SamplesPerChannel;
                    bytes += pcm.Length;
                    frames++;
                }
            }
            await session.DisposeAsync();
            if (frames == 0 || allocator.AllocatedBytes != 0)
            {
                throw new InvalidDataException("Empty audio output or retained decoder storage.");
            }
            string digest = Convert.ToHexStringLower(hash.GetHashAndReset());
            await using (FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            await using (StreamWriter writer = new(output))
            {
                await writer.WriteLineAsync(FormattableString.Invariant($"frames={frames}; samples={samples}; bytes={bytes}; sha256={digest}"));
            }
            watch.Stop();
            cpu = process.TotalProcessorTime - cpu;
            allocated = GC.GetTotalAllocatedBytes(true) - allocated;
            process.Refresh();
            await Console.Out.WriteLineAsync(FormattableString.Invariant($"samples={samples}; eof=true; retained_bytes=0; elapsed-ms={watch.Elapsed.TotalMilliseconds:F3}; cpu-ms={cpu.TotalMilliseconds:F3}; allocated-bytes={allocated}; peak-rss-bytes={process.PeakWorkingSet64}"));
        }
    }
}

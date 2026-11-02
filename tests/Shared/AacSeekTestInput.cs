// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Mediana.Codecs.Aac;
using Mediana.Containers.Mp4;
using Mediana.Core.Audio;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class AacSeekTestInput
    {
        public static async Task VerifyAsync(string root, string name, Mp4TimestampMode mode)
        {
            byte[] independent = await File.ReadAllBytesAsync(Path.Combine(root, name + ".f32le"));
            MediaEngine engine = new MediaEngineBuilder()
                .AddContainer(new Mp4ReaderFactory(new Mp4ReadOptions { TimestampMode = mode }))
                .AddAudioDecoder(new AacDecoderFactory()).Build();
            await using FileStream file = File.OpenRead(Path.Combine(root, name + ".m4a"));
            await using RandomAccessStreamByteSource source = new(file, leaveOpen: true);
            await using AudioSession session = await engine.OpenAudioAsync(source,
                new MediaOpenOptions { BufferBudgetBytes = 1024 * 1024 });
            List<(MediaTimestamp Timestamp, byte[] Bytes)> reference = [];
            int offset = 0;
            while (true)
            {
                using AudioFrame? frame = await session.ReadFrameAsync();
                if (frame is null)
                {
                    break;
                }
                ReadOnlySpan<float> samples = MemoryMarshal.Cast<byte, float>(frame.GetPlane(0).Span);
                foreach (float sample in samples)
                {
                    float expected = BinaryPrimitives.ReadSingleLittleEndian(independent.AsSpan(offset, sizeof(float)));
                    if (!float.IsFinite(sample) || Math.Abs(sample - expected) > 2e-6)
                    {
                        throw new InvalidDataException("Sequential AAC differs from the independent PCM reference.");
                    }
                    offset += sizeof(float);
                }
                reference.Add((frame.Timestamp ?? throw new InvalidDataException("AAC output has no timestamp."), frame.GetPlane(0).ToArray()));
            }
            if (offset != independent.Length || !session.CanSeek)
            {
                throw new InvalidDataException("AAC seek reference length or indexed capability changed.");
            }
            AudioFrame? retained = null;
            try
            {
                foreach (int candidate in Enumerable.Range(0, reference.Count).Reverse().Concat([0, reference.Count - 1, 0]))
                {
                    await session.SeekAsync(reference[candidate].Timestamp);
                    for (int index = candidate; index < reference.Count; index++)
                    {
                        using AudioFrame frame = await session.ReadFrameAsync() ?? throw new InvalidDataException("AAC seek lost output.");
                        if (frame.Timestamp != reference[index].Timestamp || !frame.GetPlane(0).Span.SequenceEqual(reference[index].Bytes))
                        {
                            throw new InvalidDataException("AAC seek differs from sequential PCM or its original timestamp.");
                        }
                        retained ??= frame.Retain();
                    }
                    if (await session.ReadFrameAsync() is not null)
                    {
                        throw new InvalidDataException("AAC seek produced additional output past EOF.");
                    }
                }
                await session.DisposeAsync();
                if (retained is null || !retained.GetPlane(0).Span.SequenceEqual(reference[^1].Bytes) ||
                    session.AllocatedBufferBytes != retained.GetPlane(0).Length || !file.CanRead || file.Position != 0)
                {
                    throw new InvalidOperationException("AAC seek changed retained ownership or the borrowed source.");
                }
            }
            finally
            {
                retained?.Dispose();
            }
            if (session.AllocatedBufferBytes != 0)
            {
                throw new InvalidOperationException("AAC seek retained media buffers after disposal.");
            }
        }
    }
}

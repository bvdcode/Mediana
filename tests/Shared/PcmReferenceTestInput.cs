// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class PcmReferenceTestInput
    {
        public static async Task VerifyAsync(MediaEngine engine, string inputPath, byte[] expected, AudioFormat expectedFormat)
        {
            if (!BitConverter.IsLittleEndian)
            {
                for (int index = 0; index < expected.Length; index += expectedFormat.BytesPerSample)
                {
                    Array.Reverse(expected, index, expectedFormat.BytesPerSample);
                }
            }
            int sampleBytes = expectedFormat.BytesPerSample * expectedFormat.Channels.Count;
            int samples = expected.Length / sampleBytes;
            await using RandomAccessStreamByteSource source = new(File.OpenRead(inputPath));
            await using AudioSession session = await engine.OpenAudioAsync(source, 0, new MediaOpenOptions { BufferBudgetBytes = 16384 });
            AudioFrame? retained = null;
            int position = 0;
            try
            {
                while (true)
                {
                    using AudioFrame? frame = await session.ReadFrameAsync();
                    if (frame is null)
                    {
                        break;
                    }
                    int bytes = frame.SamplesPerChannel * sampleBytes;
                    if (frame.Format.SampleFormat != expectedFormat.SampleFormat || frame.Format.SampleRate != expectedFormat.SampleRate || !frame.Format.Channels.SequenceEqual(expectedFormat.Channels)
                        || frame.Format.Layout != AudioStorageLayout.Interleaved || frame.SamplesPerChannel > 127
                        || !frame.GetPlane(0).Span.SequenceEqual(expected.AsSpan(position * sampleBytes, bytes))
                        || frame.Timestamp != new MediaTimestamp(position, new TimeBase(1, expectedFormat.SampleRate))
                        || frame.Duration != new MediaDuration(frame.SamplesPerChannel, new TimeBase(1, expectedFormat.SampleRate)))
                    {
                        throw new InvalidDataException($"PCM mismatch at sample {position} in {Path.GetFileName(inputPath)}.");
                    }
                    position += frame.SamplesPerChannel;
                    retained?.Dispose();
                    retained = frame.Retain();
                }
                if (position != samples || await session.ReadFrameAsync() is not null)
                {
                    throw new InvalidDataException("PCM changed its sample count or end of stream.");
                }
                await session.DisposeAsync();
                if (retained is null || session.AllocatedBufferBytes != retained.GetPlane(0).Length
                    || !retained.GetPlane(0).Span.SequenceEqual(expected.AsSpan(expected.Length - retained.GetPlane(0).Length)))
                {
                    throw new InvalidOperationException("PCM frame ownership did not survive audio session disposal.");
                }
            }
            finally
            {
                retained?.Dispose();
            }
            if (session.AllocatedBufferBytes != 0)
            {
                throw new InvalidOperationException("The public audio session retained PCM storage.");
            }
        }
    }
}

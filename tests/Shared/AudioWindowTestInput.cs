// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Processing.Audio;

namespace Mediana.Tests
{
    internal static class AudioWindowTestInput
    {
        public static async Task VerifyAsync(AudioStorageLayout layout, long skip, long? maximumCount,
            int expectedCount, bool rebase)
        {
            MediaBufferAllocator inputAllocator = new(4096);
            MediaBufferAllocator outputAllocator = new(4096);
            AudioSignalSource source = new(inputAllocator, 48000, 40, (index, channel) => index + channel * 100,
                [3, 7, 5], layout, ["left", "right"]);
            MediaTimestamp? origin = rebase ? new MediaTimestamp(1, new TimeBase(1, 7)) : null;
            await using AudioTrimmer window = AudioTrimmer.CreateWindow(source.ReadAsync, skip, maximumCount, outputAllocator, origin);
            int position = 0;
            AudioFrame? retained = null;
            try
            {
                while (true)
                {
                    using AudioFrame? frame = await window.ReadFrameAsync();
                    if (frame is null)
                    {
                        break;
                    }
                    Require(frame.Format == source.Format && frame.SamplesPerChannel <= expectedCount - position);
                    Require(frame.Timestamp == (rebase
                        ? new MediaTimestamp(48000 + position * 7, new TimeBase(1, 336000))
                        : new MediaTimestamp(skip + position - 7, new TimeBase(1, 48000))));
                    Require(frame.Duration == new MediaDuration(frame.SamplesPerChannel, new TimeBase(1, 48000)));
                    for (int sample = 0; sample < frame.SamplesPerChannel; sample++)
                    {
                        for (int channel = 0; channel < 2; channel++)
                        {
                            Require(ReadSample(frame, sample, channel) == skip + position + sample + channel * 100);
                        }
                    }
                    retained?.Dispose();
                    int bytes = frame.Format.GetBufferSize(frame.SamplesPerChannel);
                    bool whole = frame.SamplesPerChannel == source.LastFrame!.SamplesPerChannel;
                    Require(inputAllocator.AllocatedBytes == (whole ? bytes : 0));
                    Require(outputAllocator.AllocatedBytes == (whole ? 0 : bytes));
                    retained = frame.Retain();
                    position += frame.SamplesPerChannel;
                }
                Require(position == expectedCount);
                int blocksRead = source.BlocksRead;
                Require(await window.ReadFrameAsync() is null && source.BlocksRead == blocksRead);
                if (maximumCount == 0)
                {
                    Require(blocksRead == 0);
                }
                await window.DisposeAsync();
                Require(inputAllocator.AllocatedBytes + outputAllocator.AllocatedBytes ==
                    (retained?.Format.GetBufferSize(retained.SamplesPerChannel) ?? 0));
                if (retained is not null)
                {
                    Require(ReadSample(retained, retained.SamplesPerChannel - 1, 1) == skip + expectedCount - 1 + 100);
                }
                if (skip + expectedCount < source.Samples && maximumCount is > 0)
                {
                    using AudioFrame? unread = await source.ReadAsync(default);
                    Require(unread is not null);
                }
            }
            finally
            {
                retained?.Dispose();
            }
            Require(inputAllocator.AllocatedBytes == 0 && outputAllocator.AllocatedBytes == 0);
        }

        public static async Task VerifyWholeBlocksAsync(AudioStorageLayout layout, bool rebase)
        {
            const int BlockSamples = 8;
            AudioFormat format = new(48000, AudioSampleFormat.Float64, layout, "left", "right");
            MediaBufferAllocator inputAllocator = new(format.GetBufferSize(BlockSamples));
            MediaBufferAllocator outputAllocator = new(1);
            AudioSignalSource source = new(inputAllocator, format.SampleRate, 2 * BlockSamples,
                (sample, channel) => sample + channel * 100, [BlockSamples], layout, ["left", "right"]);
            ReadOnlyMemory<byte>[] storage = new ReadOnlyMemory<byte>[format.PlaneCount];
            MediaTimestamp? origin = rebase ? new MediaTimestamp(1, new TimeBase(1, 7)) : null;
            await using AudioTrimmer window = AudioTrimmer.CreateWindow(ReadAsync, 0, 2 * BlockSamples, outputAllocator, origin);
            for (int block = 0; block < 2; block++)
            {
                using AudioFrame frame = await window.ReadFrameAsync() ?? throw new InvalidDataException("Missing whole PCM block.");
                Require(!ReferenceEquals(frame, source.LastFrame));
                Require(frame.SamplesPerChannel == BlockSamples && frame.Duration == source.LastFrame!.Duration);
                Require(frame.Timestamp == (rebase
                    ? new MediaTimestamp(48000 + block * BlockSamples * 7, new TimeBase(1, 336000))
                    : new MediaTimestamp(block * BlockSamples - 7, new TimeBase(1, 48000))));
                for (int plane = 0; plane < format.PlaneCount; plane++)
                {
                    Require(frame.GetPlane(plane).Equals(storage[plane]));
                }
                Require(inputAllocator.AllocatedBytes == format.GetBufferSize(BlockSamples) && outputAllocator.AllocatedBytes == 0);
            }
            Require(await window.ReadFrameAsync() is null);
            Require(inputAllocator.AllocatedBytes == 0 && outputAllocator.AllocatedBytes == 0);

            async ValueTask<AudioFrame?> ReadAsync(CancellationToken token)
            {
                AudioFrame? frame = await source.ReadAsync(token);
                if (frame is not null)
                {
                    for (int plane = 0; plane < frame.Format.PlaneCount; plane++)
                    {
                        storage[plane] = frame.GetPlane(plane);
                    }
                }
                return frame;
            }
        }

        public static async Task VerifySourceFailureAsync()
        {
            MediaBufferAllocator allocator = new(4096);
            AudioSignalSource source = new(allocator, 8000, 4, (index, _) => index, [4]);
            InvalidDataException failure = new("The PCM source is truncated.");
            async ValueTask<AudioFrame?> ReadAsync(CancellationToken token)
            {
                return await source.ReadAsync(token) ?? throw failure;
            }
            await using AudioTrimmer window = AudioTrimmer.CreateWindow(ReadAsync, 0, 100, allocator);
            using (AudioFrame? frame = await window.ReadFrameAsync())
            {
                Require(frame?.SamplesPerChannel == 4);
            }
            bool propagated = false;
            try
            {
                using AudioFrame? unexpected = await window.ReadFrameAsync();
            }
            catch (InvalidDataException error) when (ReferenceEquals(error, failure))
            {
                propagated = true;
            }
            Require(propagated && allocator.AllocatedBytes == 0);
            bool terminal = false;
            try
            {
                using AudioFrame? unexpected = await window.ReadFrameAsync();
            }
            catch (InvalidOperationException)
            {
                terminal = true;
            }
            Require(terminal && allocator.AllocatedBytes == 0);
        }

        private static double ReadSample(AudioFrame frame, int sample, int channel)
        {
            return frame.Format.Layout switch
            {
                AudioStorageLayout.Interleaved => MemoryMarshal.Cast<byte, double>(frame.GetPlane(0).Span)[sample * frame.Format.Channels.Count + channel],
                AudioStorageLayout.Planar => MemoryMarshal.Cast<byte, double>(frame.GetPlane(channel).Span)[sample],
                _ => throw new ArgumentOutOfRangeException(nameof(frame))
            };
        }

        private static void Require(bool condition)
        {
            if (!condition)
            {
                throw new InvalidDataException("The available audio window changed samples, timing, source failures or storage ownership.");
            }
        }
    }
}

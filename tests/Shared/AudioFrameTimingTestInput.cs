// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Tests
{
    internal static class AudioFrameTimingTestInput
    {
        public static void Verify(AudioSampleFormat sampleFormat, AudioStorageLayout layout)
        {
            const int Samples = 8193;
            AudioFormat format = new(48000, sampleFormat, layout, "right", "left", "center");
            int bytes = format.GetBufferSize(Samples);
            MediaBufferAllocator allocator = new(bytes);
            using AudioFrame input = AudioSamplePreparationTestInput.CreateFrame(format, Samples, allocator);
            MediaTimestamp original = input.Timestamp!.Value;
            MediaTimestamp target = new(13, new TimeBase(1, 7));
            using AudioFrame changed = input.RetainWithTimestamp(target);
            using AudioFrame unknown = changed.RetainWithTimestamp(null);
            Require(input.Timestamp == original && changed.Timestamp == target && unknown.Timestamp is null);
            Require(!ReferenceEquals(input, changed) && ReferenceEquals(input.Format, changed.Format));
            Require(changed.SamplesPerChannel == Samples && changed.Duration == input.Duration);
            for (int plane = 0; plane < format.PlaneCount; plane++)
            {
                Require(input.GetPlane(plane).Equals(changed.GetPlane(plane)));
                Require(input.GetPlane(plane).Equals(unknown.GetPlane(plane)));
            }
            Require(allocator.AllocatedBytes == bytes);
            using AudioFrame retained = unknown.Retain();
            input.Dispose();
            changed.Dispose();
            unknown.Dispose();
            Require(retained.Timestamp is null && retained.GetPlane(format.PlaneCount - 1).Length == bytes / format.PlaneCount);
            Require(allocator.AllocatedBytes == bytes);
            retained.Dispose();
            Require(allocator.AllocatedBytes == 0);
        }

        private static void Require(bool condition)
        {
            if (!condition)
            {
                throw new InvalidDataException("PCM retiming changed samples, storage, duration or independent lease ownership.");
            }
        }
    }
}

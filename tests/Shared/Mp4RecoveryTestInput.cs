// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class Mp4RecoveryTestInput
    {
        public static async Task VerifyAsync(string root)
        {
            foreach (string name in new[] { "lc-mono-48.m4a", "lc-stereo-44.m4a", "lc-pns-48.m4a" })
            {
                await VerifyFileAsync(root, name, Mp4TimestampMode.Media);
                await VerifyFileAsync(root, name, Mp4TimestampMode.LinearMovie);
            }
        }

        private static async Task VerifyFileAsync(string root, string name, Mp4TimestampMode mode)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, name)));
            Mp4SampleRecoveryGroup group;
            int sampleCount;
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator,
                new Mp4ReadOptions { TimestampMode = mode }))
            {
                Mp4TrackInfo track = reader.Tracks[0];
                group = track.RecoveryGroups.Single();
                sampleCount = track.SampleCount;
                if (group.Kind != Mp4RecoveryKind.Roll || group.SampleCount != sampleCount || group.DefaultDistance is not null)
                {
                    throw new InvalidDataException("The real AAC roll grouping differs from its independent declarations.");
                }
                using MediaPacket packet = await reader.ReadPacketAtAsync(0, sampleCount - 1);
                if (packet.Data.IsEmpty)
                {
                    throw new InvalidDataException("Recovery metadata changed packet access.");
                }
            }
            for (int index = 0; index < sampleCount; index++)
            {
                if (group.GetDistance(index) != -1)
                {
                    throw new InvalidDataException("The real AAC sample requires one preceding encoded packet.");
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("Recovery metadata retained reader media storage.");
            }
        }
    }
}

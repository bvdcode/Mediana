// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Containers.Mp4
{
    internal static class Mp4FragmentHeader
    {
        public static MediaBuffer Create(IReadOnlyList<Mp4FragmentTrack> tracks, uint sequence,
            MediaBufferAllocator allocator, CancellationToken token)
        {
            Mp4BoxWriter measure = new(default);
            long payload = Build(measure, tracks, sequence, 0, token);
            int moofBytes = measure.Position;
            int mdatBytes = payload <= uint.MaxValue - 8 ? 8 : 16;
            using WritableMediaBuffer storage = allocator.Allocate(checked(moofBytes + mdatBytes));
            Mp4BoxWriter writer = new(storage.Memory);
            Build(writer, tracks, sequence, checked(moofBytes + mdatBytes), token);
            writer.UInt32(mdatBytes == 8 ? checked((uint)(payload + 8)) : 1);
            writer.Bytes("mdat"u8);
            if (mdatBytes == 16) { writer.UInt64(checked((ulong)payload + 16)); }
            return storage.Publish();
        }

        private static long Build(Mp4BoxWriter writer, IReadOnlyList<Mp4FragmentTrack> tracks,
            uint sequence, int payloadOffset, CancellationToken token)
        {
            int moof = writer.Begin("moof"), mfhd = writer.Full("mfhd");
            writer.UInt32(sequence);
            writer.End(mfhd);
            long bytes = 0;
            foreach (Mp4FragmentTrack track in tracks)
            {
                token.ThrowIfCancellationRequested();
                int traf = writer.Begin("traf"), tfhd = writer.Full("tfhd", 0x020000);
                writer.UInt32(track.TrackId);
                writer.End(tfhd);
                int tfdt = writer.Full("tfdt", 0x01000000);
                writer.UInt64(track.DecodeTime);
                writer.End(tfdt);
                bool signed = false;
                for (int index = 0; index < track.SampleCount; index++)
                {
                    signed |= track.GetSample(index, token).CompositionOffset < 0;
                }
                int trun = writer.Full("trun", (signed ? 0x01000000u : 0) | 0x000F01);
                writer.UInt32((uint)track.SampleCount);
                writer.UInt32(checked((uint)(int)(payloadOffset + bytes)));
                for (int index = 0; index < track.SampleCount; index++)
                {
                    Mp4FragmentSample sample = track.GetSample(index, token);
                    writer.UInt32(sample.Duration);
                    writer.UInt32(checked((uint)sample.Size));
                    writer.UInt32(sample.IsSync ? 0x02000000u : 0x01010000u);
                    writer.UInt32(signed ? unchecked((uint)checked((int)sample.CompositionOffset)) : checked((uint)sample.CompositionOffset));
                    bytes = checked(bytes + sample.Size);
                }
                writer.End(trun);
                if (track.Recovery is Mp4RecoveryDescription recovery)
                { Mp4RecoveryGroupWriter.WriteMapping(writer, recovery, track.SampleCount); }
                writer.End(traf);
            }
            writer.End(moof);
            return bytes;
        }
    }
}

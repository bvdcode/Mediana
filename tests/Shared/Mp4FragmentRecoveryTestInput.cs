// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Formats;
using Mediana.Formats.Audio;

namespace Mediana.Tests
{
    internal static class Mp4FragmentRecoveryTestInput
    {
        public static async Task VerifyAsync()
        {
            MediaBufferAllocator allocator = new(4096);
            foreach (Mp4RecoveryKind kind in new[] { Mp4RecoveryKind.Roll, Mp4RecoveryKind.PreRoll })
            {
                foreach (short distance in new short[] { -1, 1, short.MinValue, short.MaxValue })
                { await VerifyTrackAsync(new(kind, distance), allocator); }
            }
            await VerifyTrackAsync(null, allocator);
            if (allocator.AllocatedBytes != 0)
            { throw new InvalidOperationException("Fragment recovery metadata retained media storage."); }
        }

        private static async Task VerifyTrackAsync(Mp4RecoveryDescription? recovery, MediaBufferAllocator allocator)
        {
            EncodedStreamInfo stream = new(7, MediaKind.Audio, "aac", StreamFormats.Aac, new(1, 48000),
                AacConfiguration.Create(48000, 1).ToAudioSpecificConfig());
            Mp4AudioTrackDefinition track = new(stream, 13, 1024) { Recovery = recovery };
            using MemoryStream initialization = new();
            await Mp4EncodedFragmentWriter.WriteInitializationAsync([track], initialization, allocator);
            ReadOnlyMemory<byte> table = Mp4TestBoxes.Get(initialization.ToArray(), "moov", "trak", "mdia", "minf", "stbl");
            IReadOnlyList<ReadOnlyMemory<byte>> descriptions = Mp4TestBoxes.GetAll(table, "sgpd");
            if (Mp4TestBoxes.GetAll(table, "sbgp").Count != 0 || descriptions.Count != (recovery is null ? 0 : 1))
            { throw new InvalidDataException("The initialization must declare groups without mapping nonexistent samples."); }
            if (recovery is not null)
            {
                ReadOnlyMemory<byte> description = descriptions[0];
                if (description.Length != 18 || Mp4TestBoxes.UInt32(description, 0) != 0x01000000 ||
                    Mp4TestBoxes.UInt32(description, 4) != GroupingType(recovery.Kind) ||
                    Mp4TestBoxes.UInt32(description, 8) != 2 || Mp4TestBoxes.UInt32(description, 12) != 1 ||
                    BinaryPrimitives.ReadInt16BigEndian(description.Span[16..]) != recovery.Distance)
                { throw new InvalidDataException("The version-one recovery description differs from ISO BMFF syntax."); }
            }
            using WritableMediaBuffer storage = allocator.Allocate(3);
            storage.Span.Fill(42);
            using MediaBuffer payload = storage.Publish();
            using MediaPacket first = new(7, payload, new(-1024, new(1, 48000)), new(-1024, new(1, 48000)), new(1024, new(1, 48000)));
            using MediaPacket second = new(7, payload, new(0, new(1, 48000)), new(0, new(1, 48000)), new(1024, new(1, 48000)));
            using MediaPacket third = new(7, payload, new(1024, new(1, 48000)), new(1024, new(1, 48000)), new(1024, new(1, 48000)));
            foreach (MediaPacket[] packets in new MediaPacket[][] { [first], [first, second, third] })
            {
                using MemoryStream fragment = new();
                await Mp4EncodedFragmentWriter.WriteFragmentAsync([new(track, packets)], 19, fragment, allocator);
                ReadOnlyMemory<byte> bytes = fragment.ToArray();
                ReadOnlyMemory<byte> traf = Mp4TestBoxes.Get(bytes, "moof", "traf");
                IReadOnlyList<ReadOnlyMemory<byte>> mappings = Mp4TestBoxes.GetAll(traf, "sbgp");
                if (Mp4TestBoxes.GetAll(traf, "sgpd").Count != 0 || mappings.Count != (recovery is null ? 0 : 1))
                { throw new InvalidDataException("A fragment must reference its track's initialization description."); }
                if (recovery is not null)
                {
                    ReadOnlyMemory<byte> mapping = mappings[0];
                    if (mapping.Length != 20 || Mp4TestBoxes.UInt32(mapping, 0) != 0 ||
                        Mp4TestBoxes.UInt32(mapping, 4) != GroupingType(recovery.Kind) ||
                        Mp4TestBoxes.UInt32(mapping, 8) != 1 || Mp4TestBoxes.UInt32(mapping, 12) != packets.Length ||
                        Mp4TestBoxes.UInt32(mapping, 16) != 1)
                    { throw new InvalidDataException("Fragment recovery must map every sample to global description one."); }
                }
                ReadOnlyMemory<byte> run = Mp4TestBoxes.Get(traf, "trun");
                int offset = checked((int)Mp4TestBoxes.UInt32(run, 8));
                if (Mp4TestBoxes.UInt32(run, 4) != packets.Length ||
                    Mp4TestBoxes.UInt64(Mp4TestBoxes.Get(traf, "tfdt"), 4) != 0 ||
                    Mp4TestBoxes.Get(bytes, "mdat").Length != packets.Length * 3 || first.DecodeTime!.Value.Value != -1024)
                { throw new InvalidDataException("Recovery metadata changed fragment timing or packet ownership."); }
                foreach (MediaPacket packet in packets)
                {
                    if (!bytes.Slice(offset, packet.Data.Length).Span.SequenceEqual(packet.Data.Span))
                    { throw new InvalidDataException("Recovery metadata changed the fragment payload offset."); }
                    offset += packet.Data.Length;
                }
                if (offset != bytes.Length)
                { throw new InvalidDataException("The recovery fragment has an inconsistent payload extent."); }
            }
        }

        private static uint GroupingType(Mp4RecoveryKind kind) => kind switch
        {
            Mp4RecoveryKind.Roll => 0x726f6c6c,
            Mp4RecoveryKind.PreRoll => 0x70726f6c,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }
}

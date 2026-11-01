// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Matroska;
using Mediana.Core.Memory;
using Mediana.Formats;
using Mediana.Formats.Audio;
using static Mediana.Tests.MatroskaMetadataTestData;
using static Mediana.Tests.MatroskaIndexTestData;
using static Mediana.Tests.MatroskaPacketTestData;

namespace Mediana.Tests
{
    internal static class MatroskaCodecTestInput
    {
        public static async Task VerifyMappingsAsync()
        {
            byte[] asc = AacConfiguration.Create(48000, 1).ToAudioSpecificConfig();
            byte[] data = Data(asc, Cluster(Unsigned(0xE7, 0), Block()));
            MediaBufferAllocator allocator = new(131072);
            await using EbmlElementTestSource source = new(data) { MaximumRead = 1 };
            MatroskaTrack track = AudioTrack(asc);
            using (MatroskaPacketIndex index = await BuildAsync(source, allocator, [track]))
            {
                long retained = allocator.AllocatedBytes;
                IReadOnlyList<MatroskaCodecConfiguration> result = await ReadCodecsAsync(source, allocator, [track], index);
                Require(result.Count == 1 && result[0].Stream.Configuration.Span.SequenceEqual(asc));
                Require(result[0].Stream.Format == StreamFormats.Aac && result[0].FrameDuration == AacStream().FrameDuration);
                Require(allocator.AllocatedBytes == retained);
                data.AsSpan((int)track.CodecPrivate!.Value.DataOffset, asc.Length).Clear();
                Require(result[0].Stream.Configuration.Span.SequenceEqual(asc));
            }
            Require(allocator.AllocatedBytes == 0 && !source.IsDisposed);

            byte[] frame = new byte[384];
            new byte[] { 0xFF, 0xFB, 0x94, 0xC0 }.CopyTo(frame, 0);
            data = Data([], Cluster(Unsigned(0xE7, 0), Block(payload: frame)));
            await using EbmlElementTestSource mpegSource = new(data) { MaximumRead = 2 };
            MatroskaTrack mpeg = AudioTrack([]) with { CodecId = "A_MPEG/L3", CodecPrivate = new(0x63A2, -1, long.MaxValue) };
            using (MatroskaPacketIndex index = await BuildAsync(mpegSource, allocator, [mpeg]))
            {
                IReadOnlyList<MatroskaCodecConfiguration> result = await ReadCodecsAsync(mpegSource, allocator, [mpeg], index, 4, 4);
                Require(result[0].Stream.Format == StreamFormats.Mp3 && result[0].Stream.Configuration.Span.SequenceEqual(frame.AsSpan(0, 4)));
                result[0].ValidatePacket(frame);
                byte[] changed = new byte[417];
                new byte[] { 0xFF, 0xFB, 0x90, 0xC0 }.CopyTo(changed, 0);
                await RejectAsync<NotSupportedException>(() => { result[0].ValidatePacket(changed); return Task.CompletedTask; });
            }
            Require(allocator.AllocatedBytes == 0);
        }

        public static async Task VerifyInvalidAsync()
        {
            byte[] asc = [0x11, 0x88];
            MatroskaTrack valid = AudioTrack(asc);
            foreach (MatroskaTrack invalid in new[]
            {
                valid with { Audio = null }, valid with { Type = 1 }, valid with { CodecId = "A_OPUS" },
                valid with { CodecId = "A_AAC/MPEG4/LC" }, valid with { CodecPrivate = null },
                valid with { CodecPrivate = new(0x63A2, 1, 1) }, valid with { CodecPrivate = new(0x63A2, 1, long.MaxValue) },
                valid with { ContentEncodings = new(0x6D80, 0, 0) }, valid with { TrackOperation = new(0xE2, 0, 0) }
            })
            {
                await RejectMappingAsync(Data(asc), [invalid]);
            }
            await RejectMappingAsync(Data([0x11]), [AudioTrack([0x11])]);
            await RejectMappingAsync(Data([0x17, 0x08]), [AudioTrack([0x17, 0x08])]);
            await RejectMappingAsync(Data(asc), [valid], maximumBytes: 1);
            await RejectMappingAsync(Data(asc), [valid, valid with { Number = 2 }], maximumTotalBytes: 3);
            await RejectMappingAsync(Data([]), [AudioTrack([]) with { CodecId = "A_MPEG/L3" }]);
            await RejectMappingAsync(Data([], Cluster(Unsigned(0xE7, 0), Block(payload: [0xFF]))),
                [AudioTrack([]) with { CodecId = "A_MPEG/L3" }]);
            byte[] avc = [1, 0, 0];
            await RejectMappingAsync(Data(avc), [AudioTrack(avc) with
            {
                Type = 1, CodecId = "V_MPEG4/ISO/AVC", Audio = null, Video = new() { PixelWidth = 1, PixelHeight = 1 }
            }]);
        }

        private static async Task RejectMappingAsync(byte[] data, IReadOnlyList<MatroskaTrack> tracks,
            int maximumBytes = 65536, int maximumTotalBytes = 1048576)
        {
            MediaBufferAllocator allocator = new(131072);
            await using EbmlElementTestSource source = new(data) { MaximumRead = 1 };
            using (MatroskaPacketIndex index = await BuildAsync(source, allocator, tracks))
            {
                long before = allocator.AllocatedBytes;
                bool failed = false;
                try
                {
                    await ReadCodecsAsync(source, allocator, tracks, index, maximumBytes, maximumTotalBytes);
                }
                catch (Exception exception) when (exception is InvalidDataException or NotSupportedException)
                {
                    failed = true;
                }
                Require(failed && allocator.AllocatedBytes == before && !source.IsDisposed);
            }
            Require(allocator.AllocatedBytes == 0);
        }

        public static async Task VerifyResourcesAsync()
        {
            byte[] asc = [0x11, 0x88];
            byte[] data = Data(asc);
            TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await using EbmlElementTestSource source = new(data) { ReadGate = gate.Task, ReadGateOffset = 0 };
            MediaBufferAllocator allocator = new(16384);
            using (MatroskaPacketIndex index = new(allocator, [AudioTrack(asc)], Options))
            {
                index.Complete();
                using CancellationTokenSource cancellation = new();
                Task<IReadOnlyList<MatroskaCodecConfiguration>> pending = ReadCodecsAsync(source, allocator, [AudioTrack(asc)], index,
                    token: cancellation.Token).AsTask();
                Require(!pending.IsCompleted && allocator.AllocatedBytes == 2);
                await cancellation.CancelAsync();
                await RejectAsync<OperationCanceledException>(async () => { await pending; });
                Require(allocator.AllocatedBytes == 0 && !source.IsDisposed);
                gate.SetResult();
                Require((await ReadCodecsAsync(source, allocator, [AudioTrack(asc)], index)).Count == 1);
                MediaBufferAllocator tiny = new(1);
                await RejectAsync<MediaMemoryLimitException>(async () => { await ReadCodecsAsync(source, tiny, [AudioTrack(asc)], index); });
                Require(tiny.AllocatedBytes == 0);
            }
        }
    }
}

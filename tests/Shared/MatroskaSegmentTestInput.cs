// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Containers.Matroska;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class MatroskaSegmentTestInput
    {
        public static async Task VerifyUnknownClustersAsync()
        {
            uint[] boundaries = [0x114D9B74, 0x1549A966, 0x1654AE6B, 0x1C53BB6B, 0x1941A469, 0x1043A770, 0x1254C367, 0x1F43B675];
            foreach (bool knownParent in new[] { false, true })
            {
                foreach (uint boundary in boundaries)
                {
                    byte[] input = [0x18, 0x53, 0x80, 0x67, 0xFF, 0x1F, 0x43, 0xB6, 0x75, 0xFF,
                        0xE7, 0x81, 0, 0xEC, 0x80, 0xA3, 0x85, 0x1F, 0x43, 0xB6, 0x75, 0xFF,
                        (byte)(boundary >> 24), (byte)(boundary >> 16), (byte)(boundary >> 8), (byte)boundary, 0x80];
                    if (knownParent)
                    {
                        input[4] = (byte)(0x80 | (input.Length - 5));
                    }
                    await using EbmlElementTestSource source = new(input) { MaximumRead = 1 };
                    MediaBufferAllocator allocator = new(12);
                    using (MatroskaSegmentReader reader = new(source, allocator, 8))
                    {
                        EbmlElement segment = await reader.ReadRootAsync(0);
                        Require(segment.Id == 0x18538067 && segment.DataOffset == 5);
                        Require(knownParent == (segment.EndOffset == input.Length));
                        EbmlElement cluster = await reader.ReadChildAsync(segment, 5) ?? throw new InvalidDataException();
                        Require(cluster == new EbmlElement(0x1F43B675, 10, 22));
                        EbmlElement sibling = await reader.ReadChildAsync(segment, 22) ?? throw new InvalidDataException();
                        Require(sibling.Id == boundary && sibling.DataSize == 0);
                        Require(await reader.ReadChildAsync(segment, input.Length) is null);
                        await reader.VerifyDocumentEndAsync(segment);
                        Require(allocator.AllocatedBytes == 12);
                    }
                    Require(allocator.AllocatedBytes == 0 && !source.IsDisposed);
                }
            }
        }

        public static async Task VerifyEofGlobalsAndLimitsAsync()
        {
            byte[] input = [0xEC, 0x80, 0x18, 0x53, 0x80, 0x67, 0xFF,
                0x1F, 0x43, 0xB6, 0x75, 0xFF, 0xE7, 0x81, 0, 0xBF, 0x84, 1, 2, 3, 4, 0xEC, 0x80];
            await using EbmlElementTestSource source = new(input);
            MediaBufferAllocator allocator = new(12);
            using (MatroskaSegmentReader reader = new(source, allocator, 8))
            {
                EbmlElement segment = await reader.ReadRootAsync(0);
                Require(segment.DataOffset == 7 && segment.EndOffset is null);
                Require((await reader.ReadChildAsync(segment, 7)) == new EbmlElement(0x1F43B675, 12, input.Length));
                await reader.VerifyDocumentEndAsync(segment);
                await RejectAsync<ArgumentOutOfRangeException>(async () => await reader.ReadChildAsync(segment, 6));
            }
            Require(allocator.AllocatedBytes == 0);
            byte[] trailingVoid = [0x18, 0x53, 0x80, 0x67, 0x80, 0xEC, 0x81, 0];
            await VerifyDocumentAsync(trailingVoid, 5);
            byte[] large = new byte[24];
            BinaryPrimitives.WriteUInt32BigEndian(large, 0x18538067);
            const long payload = 1L << 40;
            BinaryPrimitives.WriteUInt64BigEndian(large.AsSpan(4), (1UL << 56) | (ulong)payload);
            BinaryPrimitives.WriteUInt32BigEndian(large.AsSpan(12), 0x1F43B675);
            BinaryPrimitives.WriteUInt64BigEndian(large.AsSpan(16), (1UL << 56) | (ulong)(payload - 12));
            await using EbmlElementTestSource virtualSource = new(large) { Length = payload + 12 };
            using MatroskaSegmentReader largeReader = new(virtualSource, allocator, 8);
            EbmlElement root = await largeReader.ReadRootAsync(0);
            EbmlElement cluster = await largeReader.ReadChildAsync(root, 12) ?? throw new InvalidDataException();
            Require(cluster.DataOffset == 24 && cluster.EndOffset == payload + 12 && allocator.AllocatedBytes == 12);
            Require(virtualSource.ReadCalls == 2);
            Require(await largeReader.ReadChildAsync(root, payload + 12) is null && virtualSource.ReadCalls == 2);
        }

        public static async Task VerifyInvalidHierarchyAsync()
        {
            await RejectDocumentAsync<InvalidDataException>([0xEC, 0x80]);
            await RejectDocumentAsync<InvalidDataException>([0xEC, 0xFF]);
            await RejectDocumentAsync<InvalidDataException>([0xBF, 0x84, 0, 0, 0, 0]);
            await RejectDocumentAsync<InvalidDataException>([0x18, 0x53, 0x80, 0x67, 0x80, 0xEC, 0xFF]);
            await RejectDocumentAsync<NotSupportedException>([0x18, 0x53, 0x80, 0x67, 0x80, 0x1A, 0x45, 0xDF, 0xA3, 0x80]);
            foreach (byte[] invalidChild in new byte[][]
            {
                [0xA3, 0x80], [0x15, 0x49, 0xA9, 0x66, 0xFF], [0x11, 0x11, 0x11, 0x11, 0xFF],
                [0x1F, 0x43, 0xB6, 0x75, 0xFF, 0xA3, 0xFF],
                [0x1F, 0x43, 0xB6, 0x75, 0xFF, 0xEC, 0xFF],
                [0x1F, 0x43, 0xB6, 0x75, 0xFF, 0xA3, 0x81],
                [0x18, 0x53, 0x80, 0x67, 0x80], [0x1A, 0x45, 0xDF, 0xA3, 0x80]
            })
            {
                byte[] input = [0x18, 0x53, 0x80, 0x67, (byte)(0x80 | invalidChild.Length), .. invalidChild];
                await using EbmlElementTestSource source = new(input) { MaximumRead = 1 };
                MediaBufferAllocator allocator = new(12);
                using (MatroskaSegmentReader reader = new(source, allocator, 8))
                {
                    EbmlElement root = await reader.ReadRootAsync(0);
                    await RejectAsync<InvalidDataException>(async () => await reader.ReadChildAsync(root, root.DataOffset));
                }
                Require(allocator.AllocatedBytes == 0 && !source.IsDisposed);
            }
            await using EbmlElementTestSource unknown = new(ReadOnlyMemory<byte>.Empty) { Length = null };
            MediaBufferAllocator limited = new(12);
            try
            {
                using MatroskaSegmentReader reader = new(unknown, limited, 8);
            }
            catch (NotSupportedException)
            {
                Require(limited.AllocatedBytes == 0 && !unknown.IsDisposed);
                return;
            }
            throw new InvalidDataException("An unknown-length source was accepted for Matroska indexing.");
        }

        public static async Task VerifyCancellationAsync()
        {
            byte[] input = [0x18, 0x53, 0x80, 0x67, 0xFF, 0x1F, 0x43, 0xB6, 0x75, 0xFF, 0xE7, 0x81, 0];
            TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await using EbmlElementTestSource source = new(input) { ReadGate = gate.Task, ReadGateOffset = 10 };
            MediaBufferAllocator allocator = new(12);
            using (MatroskaSegmentReader reader = new(source, allocator, 8))
            {
                EbmlElement root = await reader.ReadRootAsync(0);
                using CancellationTokenSource pending = new();
                ValueTask<EbmlElement?> read = reader.ReadChildAsync(root, 5, pending.Token);
                Require(!read.IsCompleted);
                await pending.CancelAsync();
                await RejectAsync<OperationCanceledException>(async () => await read);
                gate.SetResult();
                Require((await reader.ReadChildAsync(root, 5))?.EndOffset == input.Length);
            }
            Require(allocator.AllocatedBytes == 0 && !source.IsDisposed);
        }

        public static async Task VerifyMuxedFileAsync()
        {
            byte[] original = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Matroska", "avc-aac-attachment.mkv"));
            foreach (bool unknown in new[] { false, true })
            {
                byte[] input = (byte[])original.Clone();
                if (unknown)
                {
                    input.AsSpan(44, 8).Fill(255);
                    input[44] = 1;
                    foreach (int sizeOffset in new[] { 2004, 9361 })
                    {
                        input[sizeOffset] = 0x7F;
                        input[sizeOffset + 1] = 255;
                    }
                }
                await using EbmlElementTestSource source = new(input) { MaximumRead = 3 };
                MediaBufferAllocator allocator = new(12);
                using (MatroskaSegmentReader reader = new(source, allocator, 8))
                {
                    EbmlElement root = await reader.ReadRootAsync(40);
                    Require(root.DataOffset == 52 && unknown == (root.EndOffset is null));
                    long offset = root.DataOffset;
                    foreach ((uint id, long start, long end) in new[]
                    {
                        (0x114D9B74u, 57L, 137L), (0xECu, 146L, 213L), (0x1549A966u, 218L, 324L),
                        (0x1654AE6Bu, 330L, 556L), (0x1941A469u, 562L, 1847L), (0x1254C367u, 1853L, 2000L),
                        (0x1F43B675u, 2006L, 9357L), (0x1F43B675u, 9363L, 18997L), (0x1C53BB6Bu, 19002L, 19043L)
                    })
                    {
                        EbmlElement child = await reader.ReadChildAsync(root, offset) ?? throw new InvalidDataException();
                        Require(child == new EbmlElement(id, start, end));
                        offset = end;
                    }
                    Require(await reader.ReadChildAsync(root, offset) is null);
                    await reader.VerifyDocumentEndAsync(root);
                }
                Require(allocator.AllocatedBytes == 0 && !source.IsDisposed);
            }
        }

        private static async Task VerifyDocumentAsync(byte[] input, long trailingOffset)
        {
            await using EbmlElementTestSource source = new(input);
            MediaBufferAllocator allocator = new(12);
            using (MatroskaSegmentReader reader = new(source, allocator, 8))
            {
                EbmlElement root = await reader.ReadRootAsync(0);
                Require(root.EndOffset == trailingOffset);
                await reader.VerifyDocumentEndAsync(root);
            }
            Require(allocator.AllocatedBytes == 0 && !source.IsDisposed);
        }

        private static async Task RejectDocumentAsync<TException>(byte[] input) where TException : Exception
        {
            await using EbmlElementTestSource source = new(input);
            MediaBufferAllocator allocator = new(12);
            using (MatroskaSegmentReader reader = new(source, allocator, 8))
            {
                await RejectAsync<TException>(async () =>
                {
                    EbmlElement root = await reader.ReadRootAsync(0);
                    await reader.VerifyDocumentEndAsync(root);
                });
            }
            Require(allocator.AllocatedBytes == 0 && !source.IsDisposed);
        }

        private static async Task RejectAsync<TException>(Func<Task> action) where TException : Exception
        {
            try
            {
                await action();
            }
            catch (TException)
            {
                return;
            }
            throw new InvalidDataException("An invalid Matroska Segment operation was accepted.");
        }

        private static void Require(bool condition)
        {
            if (!condition)
            {
                throw new InvalidDataException("Matroska Segment hierarchy, offsets or ownership changed.");
            }
        }
    }
}

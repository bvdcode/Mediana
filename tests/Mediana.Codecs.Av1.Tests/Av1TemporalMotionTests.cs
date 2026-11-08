// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using System.Runtime.CompilerServices;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1TemporalMotionTests
    {
        [Fact]
        public void NativeProjectionAndPositionVectorsMatchSignedRoundingAndBounds()
        {
            using FileStream file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "temporal.bin.gz"));
            using GZipStream expanded = new(file, CompressionMode.Decompress);
            using BinaryReader reader = new(expanded);
            Assert.Equal("AV1TMVF1"u8.ToArray(), reader.ReadBytes(8));
            Assert.Equal(4096, reader.ReadInt32());
            for (int test = 0; test < 4096; test++)
            {
                int row = reader.ReadInt16();
                int column = reader.ReadInt16();
                int numerator = reader.ReadInt16();
                int denominator = reader.ReadByte();
                int expectedRow = reader.ReadInt16();
                int expectedColumn = reader.ReadInt16();
                Assert.Equal(expectedRow, Av1MotionField.Project(row, numerator, denominator));
                Assert.Equal(expectedColumn, Av1MotionField.Project(column, numerator, denominator));
                int rows = reader.ReadByte();
                int columns = reader.ReadByte();
                int y = reader.ReadByte();
                int x = reader.ReadByte();
                int sign = reader.ReadByte() == 1 ? -1 : 1;
                bool valid = reader.ReadByte() != 0;
                int targetY = reader.ReadInt16();
                int targetX = reader.ReadInt16();
                int actualY = Av1MotionField.Position(y, expectedRow, sign, rows, 0);
                int actualX = Av1MotionField.Position(x, expectedColumn, sign, columns, 8);
                Assert.Equal(valid, actualY >= 0 && actualX >= 0);
                if (valid) { Assert.Equal(targetY, actualY); Assert.Equal(targetX, actualX); }
            }
            Assert.Equal(-1, expanded.ReadByte());
            Assert.Equal(6, Unsafe.SizeOf<Av1MotionFieldSample>());
        }

        [Fact]
        public void SharedReferenceSlotsOwnOneCompactFieldAndProjectAtLookup()
        {
            byte[] encoded = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "interOrdered.obu"));
            IReadOnlyList<ReadOnlyMemory<byte>> units = Av1SequencePictureTests.AccessUnits(encoded);
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (Av1PictureDecoder decoder = new(allocator))
            using (Av1Picture key = decoder.Decode(units[0].Span))
            {
                Assert.True(key.Sequence.Tools.OrderHintBits > 0);
                long baseline = allocator.AllocatedBytes;
                int snapshots = Av1DefaultCdfs.Count * sizeof(ushort);
                ushort[] probabilities = Av1DefaultCdfs.Values.ToArray();
                using Av1ReferencePictures references = new();
                references.Save(key, probabilities, allocator);
                Av1FrameHeader header = key.Header with
                {
                    FrameType = Av1FrameType.Inter,
                    OrderHint = 2,
                    RefreshReferences = Av1ReferencePictures.All,
                    Inter = new(default, Av1InterPrediction.NoPrimaryReference, false, true, 0, false, true)
                };
                int length = Av1MotionField.StoredLength(key.Sequence, header);
                Av1MotionBlock[] blocks = new Av1MotionBlock[key.Sequence.MiColumns * key.Sequence.MiRows];
                blocks.AsSpan().Fill(new() { Reference = 1, Row = 16, Column = -8, Width = 2, Height = 2 });
                using (Av1Picture current = new(header, key.Pixels.Retain()))
                using (WritableMediaBuffer field = allocator.Allocate(length))
                {
                    Av1MotionField.Store(key.Sequence, header, references, blocks, field.Span);
                    using MediaBuffer frozen = field.Publish();
                    references.Save(current, probabilities, allocator, frozen);
                }
                Assert.Equal(baseline + snapshots + length, allocator.AllocatedBytes);
                Assert.Same(references.Get(0), references.Get(7));
                Assert.NotNull(references.Get(7).MotionField);
                references.Invalidate(1);
                Assert.Equal(baseline + snapshots + length, allocator.AllocatedBytes);
                Av1ReferenceIndices indices = default;
                for (int index = 0; index < Av1ReferenceIndices.Count; index++) { indices[index] = 7; }
                Av1FrameHeader next = header with { OrderHint = 3, Inter = header.Inter with { References = indices } };
                Av1MotionFieldSample[] projected = new Av1MotionFieldSample[key.Sequence.MiColumns * key.Sequence.MiRows / 4];
                Av1MotionField.Estimate(key.Sequence, next, references, projected, default);
                for (int y = 0; y < key.Sequence.MiRows / 2; y++)
                {
                    for (int x = 0; x < key.Sequence.MiColumns / 2; x++)
                    {
                        Assert.True(Av1MotionField.TryGet(projected, key.Sequence, next, references, 1, y, x, out Av1MotionCandidate motion));
                        Assert.Equal(new Av1MotionCandidate(8, -4, 2), motion);
                    }
                }
                references.Invalidate(Av1ReferencePictures.All);
                Assert.Equal(baseline, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
        [Theory]
        [InlineData("temporalTx8")]
        [InlineData("temporalTx16")]
        [InlineData("temporalFree")]
        public void RealMovingSequencesSelectTemporalMotion(string name)
        {
            byte[] encoded = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", name + ".obu"));
            MediaBufferAllocator allocator = new(1024 * 1024);
            int selected = 0;
            using (Av1PictureDecoder decoder = new(allocator))
            {
                foreach (ReadOnlyMemory<byte> unit in Av1SequencePictureTests.AccessUnits(encoded))
                {
                    using Av1Picture picture = decoder.Decode(unit.Span);
                    if (picture.Header.Inter.ReferenceMotion) { selected++; }
                }
            }
            Assert.True(selected >= 3, $"Only {selected} pictures selected temporal motion.");
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void ScopedFailureReleasesStoredProjectedAndSnapshotFields(int allocation)
        {
            byte[] encoded = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "temporalTx8.obu"));
            IReadOnlyList<ReadOnlyMemory<byte>> units = Av1SequencePictureTests.AccessUnits(encoded);
            int samples = 48 * 40 * 3 / 2;
            int snapshot = Av1DefaultCdfs.Count * sizeof(ushort);
            int field = 6 * 5 * Unsafe.SizeOf<Av1MotionFieldSample>();
            int motions = 12 * 10 * Unsafe.SizeOf<Av1MotionBlock>();
            int retained = samples + snapshot;
            int reserved = allocation switch
            {
                0 => retained + samples,
                1 => retained + samples + field + motions,
                2 => retained + samples + field,
                _ => throw new ArgumentOutOfRangeException(nameof(allocation))
            };
            int requested = allocation switch
            {
                0 or 1 => field,
                2 => snapshot,
                _ => throw new ArgumentOutOfRangeException(nameof(allocation))
            };
            MediaBufferAllocator input = new(1024 * 1024);
            MediaBufferAllocator parent = new(1024 * 1024);
            MediaBufferAllocator allocator = parent.CreateScope(reserved + requested - 1);
            EncodedStreamInfo stream = new(0, MediaKind.Video, "av1", StreamFormats.Av1, new(1, 25), new byte[] { 0x81, 0, 0x0C, 0 });
            using (IMediaCodec<MediaPacket, VideoFrame> codec = new Av1DecoderFactory().Create(stream, allocator))
            using (MediaPacket key = Packet(units[0].Span, input))
            using (MediaPacket inter = Packet(units[1].Span, input))
            {
                Assert.Equal(CodecInputStatus.Accepted, codec.TrySend(key));
                codec.Receive().Value.Dispose();
                Assert.Equal(retained, allocator.AllocatedBytes);
                MediaMemoryLimitException error = Assert.Throws<MediaMemoryLimitException>(() => codec.TrySend(inter));
                Assert.Equal(requested, error.RequestedBytes);
                Assert.Equal(0, allocator.AllocatedBytes);
                Assert.Equal(0, parent.AllocatedBytes);
                Assert.Throws<InvalidOperationException>(() => codec.Receive());
                codec.Reset();
                Assert.Equal(CodecInputStatus.Accepted, codec.TrySend(key));
                codec.Receive().Value.Dispose();
            }
            Assert.Equal(0, parent.AllocatedBytes);
            Assert.Equal(0, input.AllocatedBytes);
        }

        [Fact]
        public void CancellationReleasesPersistentTemporalReferencesAndAllowsReset()
        {
            byte[] encoded = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "temporalTx8.obu"));
            IReadOnlyList<ReadOnlyMemory<byte>> units = Av1SequencePictureTests.AccessUnits(encoded);
            MediaBufferAllocator input = new(1024 * 1024);
            MediaBufferAllocator allocator = new(1024 * 1024);
            EncodedStreamInfo stream = new(0, MediaKind.Video, "av1", StreamFormats.Av1, new(1, 25), new byte[] { 0x81, 0, 0x0C, 0 });
            using (IMediaCodec<MediaPacket, VideoFrame> codec = new Av1DecoderFactory().Create(stream, allocator))
            using (MediaPacket key = Packet(units[0].Span, input))
            using (MediaPacket first = Packet(units[1].Span, input))
            using (MediaPacket second = Packet(units[2].Span, input))
            using (CancellationTokenSource cancellation = new())
            {
                Assert.Equal(CodecInputStatus.Accepted, codec.TrySend(key));
                codec.Receive().Value.Dispose();
                Assert.Equal(CodecInputStatus.Accepted, codec.TrySend(first));
                codec.Receive().Value.Dispose();
                cancellation.Cancel();
                Assert.Throws<OperationCanceledException>(() => codec.TrySend(second, cancellation.Token));
                Assert.Equal(0, allocator.AllocatedBytes);
                Assert.Throws<InvalidOperationException>(() => codec.Receive());
                codec.Reset();
                Assert.Equal(CodecInputStatus.Accepted, codec.TrySend(key));
                codec.Receive().Value.Dispose();
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Equal(0, input.AllocatedBytes);
        }

        private static MediaPacket Packet(ReadOnlySpan<byte> encoded, MediaBufferAllocator allocator)
        {
            using WritableMediaBuffer write = allocator.Allocate(encoded.Length);
            encoded.CopyTo(write.Span);
            using MediaBuffer buffer = write.Publish();
            return new(0, buffer, new(0, new(1, 25)));
        }
    }
}

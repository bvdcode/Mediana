// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Containers.Mp4;

namespace Mediana.Tests
{
    public class Mp4FragmentIndexTests
    {
        [Theory]
        [InlineData(0u, false)]
        [InlineData(0x100u, false)]
        [InlineData(0x200u, false)]
        [InlineData(0x400u, false)]
        [InlineData(0x800u, false)]
        [InlineData(0xF00u, false)]
        [InlineData(0xF00u, true)]
        public void RunDefaultsFieldsAndSparseCheckpointsPreserveEveryLocation(uint flags, bool signed)
        {
            const int count = 193;
            Mp4FragmentDefaults defaults = new(1, 40, 3, 0x10000);
            byte[] payload = Mp4TestEncoding.Write(writer =>
            {
                writer.UInt32(flags | (signed ? 0x01000000u : 0));
                writer.UInt32(count);
                for (int index = 0; index < count; index++)
                {
                    if ((flags & 0x100) != 0) { writer.UInt32((uint)(index % 5 + 1)); }
                    if ((flags & 0x200) != 0) { writer.UInt32((uint)(index % 7 + 1)); }
                    if ((flags & 0x400) != 0) { writer.UInt32(index % 11 == 0 ? 0u : 0x10000u); }
                    if ((flags & 0x800) != 0) { writer.UInt32(unchecked((uint)(signed ? index % 7 - 3 : index % 7))); }
                }
            });
            Mp4FragmentRun run = new(payload, 23, 987, 1000, 1000, defaults, count, [new(1000, 2000)], default);
            long offset = 1000;
            long time = 987;
            List<Mp4SampleLocation> expected = [];
            for (int index = 0; index < count; index++)
            {
                int size = (flags & 0x200) != 0 ? index % 7 + 1 : 3;
                uint duration = (flags & 0x100) != 0 ? (uint)(index % 5 + 1) : 40;
                int composition = (flags & 0x800) == 0 ? 0 : signed ? index % 7 - 3 : index % 7;
                bool sync = (flags & 0x400) != 0 && index % 11 == 0;
                expected.Add(new(offset, size, time, time + composition, duration, sync));
                offset += size;
                time += duration;
            }
            for (int index = count - 1; index >= 0; index--)
            {
                Assert.Equal(expected[index], run.Locate(index));
                Assert.Equal(expected.FindLastIndex(index, index + 1, sample => sample.IsSync), run.FindSyncSample(index));
                int seek = expected.FindLastIndex(index, index + 1, sample => sample.IsSync && sample.PresentationTime <= expected[index].PresentationTime);
                Assert.Equal(seek, run.FindSeekSample(expected[index].PresentationTime, index + 1, default));
            }
            Assert.Equal(offset, run.EndOffset);
            Assert.Equal(time, run.EndDecodeTime);
            Assert.Equal(time - 987, run.DecodeDurationTicks);
            Assert.Equal(offset - 1000, run.PayloadBytes);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ExplicitSignedDataOffsetAndFirstSampleFlagsOverrideDefaults(bool versionOne)
        {
            byte[] bytes = Mp4TestEncoding.Write(writer =>
            {
                writer.UInt32(versionOne ? 0x01000005u : 5u);
                writer.UInt32(3);
                writer.UInt32(unchecked((uint)-100));
                writer.UInt32(0);
            });
            Mp4FragmentRun run = new(bytes, 0, 0, 1100, 777, new(1, 1024, 3, 0x10000), 3, [new(1000, 9)], default);
            Assert.Equal(1000, run.Locate(0).Offset);
            Assert.Equal(1006, run.Locate(2).Offset);
            Assert.True(run.Locate(0).IsSync);
            Assert.False(run.Locate(1).IsSync);
            Assert.Equal(0, run.FindSyncSample(2));
        }

        [Fact]
        public void DecodeGapExtendsLastDurationWithoutChangingEarlierLocations()
        {
            byte[] bytes = Mp4TestEncoding.Write(writer => { writer.UInt32(0); writer.UInt32(2); });
            Mp4FragmentRun run = new(bytes, 0, 100, 0, 0, new(1, 40, 3, 0), 2, [new(0, 6)], default);
            run.ExtendLastDuration(20);
            Assert.Equal(new(0, 3, 100, 100, 40, true), run.Locate(0));
            Assert.Equal(new(3, 3, 140, 140, 60, true), run.Locate(1));
            Assert.Equal(200, run.EndDecodeTime);
            Assert.Equal(200, run.PresentationEnd);
            Assert.Equal(100, run.DecodeDurationTicks);
        }

        [Fact]
        public void InvalidCountsOffsetsAndCancellationFailBeforeIndexConstruction()
        {
            byte[] bytes = Mp4TestEncoding.Write(writer => { writer.UInt32(0); writer.UInt32(2); });
            Assert.Throws<NotSupportedException>(() => new Mp4FragmentRun(bytes, 0, 0, 0, 0, new(1, 1, 3, 0), 1, [new(0, 6)], default));
            Assert.Throws<InvalidDataException>(() => new Mp4FragmentRun(bytes, 0, 0, 0, 0, new(1, 1, 3, 0), 2, [new(0, 5)], default));
            Assert.Throws<OperationCanceledException>(() => new Mp4FragmentRun(bytes, 0, 0, 0, 0, new(1, 1, 3, 0), 2, [new(0, 6)], new(true)));
            BinaryPrimitives.WriteUInt32BigEndian(bytes, 0x404);
            Assert.Throws<InvalidDataException>(() => new Mp4FragmentRun(bytes, 0, 0, 0, 0, new(1, 1, 3, 0), 2, [new(0, 6)], default));
            BinaryPrimitives.WriteUInt32BigEndian(bytes, 0x100);
            Assert.Throws<InvalidDataException>(() => new Mp4FragmentRun(bytes, 0, 0, 0, 0, new(1, 1, 3, 0), 2, [new(0, 6)], default));
        }

    }
}

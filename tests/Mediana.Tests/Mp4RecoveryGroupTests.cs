// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Containers.Mp4;

namespace Mediana.Tests
{
    public class Mp4RecoveryGroupTests
    {
        [Theory]
        [InlineData(0, false)]
        [InlineData(1, false)]
        [InlineData(1, true)]
        [InlineData(2, false)]
        [InlineData(2, true)]
        public void SignedRunsExplicitNoGroupAndUnmappedDefaultsUseEncodedSampleIndices(int version, bool variable)
        {
            byte[] description = Description(version, variable, version == 2 ? 2u : 0, -1, 3);
            byte[] mapping = Mapping(1, (2u, 1u), (0u, 2u), (1u, 0u), (3u, 2u), (1u, 1u));
            Mp4SampleRecoveryGroup group = Assert.Single(Parse(description, mapping));
            Assert.Equal(Mp4RecoveryKind.Roll, group.Kind);
            Assert.Equal(10, group.SampleCount);
            short?[] expected = [-1, -1, null, 3, 3, 3, -1,
                version == 2 ? (short)3 : null, version == 2 ? (short)3 : null, version == 2 ? (short)3 : null];
            for (int index = 0; index < expected.Length; index++)
            {
                Assert.Equal(expected[index], group.GetDistance(index));
            }
            description.AsSpan().Clear();
            mapping.AsSpan().Clear();
            Assert.Equal((short)-1, group.GetDistance(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => group.GetDistance(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => group.GetDistance(10));
        }

        [Fact]
        public void DefaultOnlyAndIndependentRollAndPrerollGroupsRemainSeparate()
        {
            byte[] roll = Description(2, false, 1, -2);
            byte[] preroll = Description(1, false, 0, -7);
            "prol"u8.CopyTo(preroll.AsSpan(12));
            byte[] mapping = Mapping(0, (10u, 1u));
            "prol"u8.CopyTo(mapping.AsSpan(12));
            IReadOnlyList<Mp4SampleRecoveryGroup> groups = Parse(roll, preroll, mapping);
            Assert.Equal(2, groups.Count);
            Assert.Equal((short)-2, groups[0].GetDistance(9));
            Assert.Equal(Mp4RecoveryKind.PreRoll, groups[1].Kind);
            Assert.Equal((short)-7, groups[1].GetDistance(9));
        }

        [Fact]
        public void FullSignedRangeEmptyGroupsAndUnknownGroupTypesRemainExplicit()
        {
            Mp4SampleRecoveryGroup group = Assert.Single(Parse(Description(1, false, 0, short.MinValue, short.MaxValue),
                Mapping(0, (1u, 1u), (9u, 2u))));
            Assert.Equal(short.MinValue, group.GetDistance(0));
            Assert.Equal(short.MaxValue, group.GetDistance(9));
            group = Assert.Single(Parse(Description(2, false, 0)));
            Assert.Null(group.GetDistance(0));
            byte[] unknown = Description(1, false, 0, -1);
            "xxxx"u8.CopyTo(unknown.AsSpan(12));
            Assert.Empty(Parse(unknown));
            byte[] newer = Description(1, false, 0, -1);
            newer[8] = 3;
            Assert.Throws<NotSupportedException>(() => Parse(newer));
            byte[] flags = Description(1, false, 0, -1);
            flags[11] = 1;
            Assert.Throws<NotSupportedException>(() => Parse(flags));
            byte[] newerMapping = Mapping(0, (1u, 1u));
            newerMapping[8] = 2;
            Assert.Throws<NotSupportedException>(() => Parse(Description(1, false, 0, -1), newerMapping));
        }

        [Fact]
        public void InvalidDescriptionsMappingsAndDuplicatesFail()
        {
            byte[] valid = Description(1, false, 0, -1);
            Assert.Throws<InvalidDataException>(() => Parse(Description(1, false, 0, 0)));
            Assert.Throws<InvalidDataException>(() => Parse(Description(2, false, 2, -1)));
            Assert.Throws<InvalidDataException>(() => Parse(valid, Mapping(0, (1u, 2u))));
            Assert.Throws<InvalidDataException>(() => Parse(valid, Mapping(0, (11u, 1u))));
            Assert.Throws<InvalidDataException>(() => Parse(Mapping(0, (1u, 1u))));
            Assert.Throws<InvalidDataException>(() => Parse(valid, valid));
            byte[] mapping = Mapping(0, (1u, 1u));
            Assert.Throws<InvalidDataException>(() => Parse(valid, mapping, mapping));
            byte[] badLength = valid.ToArray();
            BinaryPrimitives.WriteUInt32BigEndian(badLength.AsSpan(16), 3);
            Assert.Throws<InvalidDataException>(() => Parse(badLength));
            byte[] variable = Description(1, true, 0, -1);
            BinaryPrimitives.WriteUInt32BigEndian(variable.AsSpan(24), 1);
            Assert.Throws<InvalidDataException>(() => Parse(variable));
            byte[] parameterized = Mapping(1, (1u, 1u));
            parameterized[19] = 1;
            Assert.Throws<NotSupportedException>(() => Parse(valid, parameterized));
        }

        [Fact]
        public void EveryTruncatedRecognizedPayloadAndUnboundedCountFailsBeforeExpansion()
        {
            foreach (byte[] original in new[] { Description(2, true, 1, -1), Mapping(1, (1u, 1u)) })
            {
                for (int length = 0; length < original.Length - 8; length++)
                {
                    byte[] truncated = original.AsSpan(0, length + 8).ToArray();
                    BinaryPrimitives.WriteUInt32BigEndian(truncated, (uint)truncated.Length);
                    Assert.Throws<InvalidDataException>(() => Parse(truncated));
                }
            }
            byte[] oversized = Description(1, false, 0, -1);
            BinaryPrimitives.WriteUInt32BigEndian(oversized.AsSpan(20), uint.MaxValue);
            Assert.Throws<NotSupportedException>(() => Parse(oversized));
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Mp4BoxCollection boxes = Boxes([]);
            Assert.Throws<OperationCanceledException>(() => Mp4RecoveryGroupParser.Parse(boxes, 10, new(), cancellation.Token));
        }

        [Fact]
        public Task RealDeclaredRollDistancesAndPacketsSurviveReaderDisposal() =>
            Mp4RecoveryTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Aac"));

        private static IReadOnlyList<Mp4SampleRecoveryGroup> Parse(params byte[][] boxes) =>
            Mp4RecoveryGroupParser.Parse(Boxes(boxes.SelectMany(box => box).ToArray()), 10, new(), default);

        private static Mp4BoxCollection Boxes(byte[] data) => new(data, new(new(), default));

        private static byte[] Description(int version, bool variable, uint defaultIndex, params short[] distances)
        {
            List<byte> payload = [(byte)version, 0, 0, 0, .. "roll"u8.ToArray()];
            if (version >= 1)
            {
                Add(payload, variable ? 0u : 2u);
            }
            if (version >= 2)
            {
                Add(payload, defaultIndex);
            }
            Add(payload, (uint)distances.Length);
            foreach (short distance in distances)
            {
                if (variable)
                {
                    Add(payload, 2);
                }
                payload.Add((byte)((ushort)distance >> 8));
                payload.Add((byte)distance);
            }
            return Box("sgpd"u8, payload);
        }

        private static byte[] Mapping(int version, params (uint Count, uint Group)[] runs)
        {
            List<byte> payload = [(byte)version, 0, 0, 0, .. "roll"u8.ToArray()];
            if (version == 1)
            {
                Add(payload, 0);
            }
            Add(payload, (uint)runs.Length);
            foreach ((uint count, uint group) in runs)
            {
                Add(payload, count);
                Add(payload, group);
            }
            return Box("sbgp"u8, payload);
        }

        private static byte[] Box(ReadOnlySpan<byte> type, List<byte> payload)
        {
            byte[] result = new byte[payload.Count + 8];
            BinaryPrimitives.WriteUInt32BigEndian(result, (uint)result.Length);
            type.CopyTo(result.AsSpan(4));
            payload.CopyTo(result, 8);
            return result;
        }

        private static void Add(List<byte> output, uint value)
        {
            output.AddRange([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);
        }
    }
}

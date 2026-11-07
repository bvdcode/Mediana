// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text.Json;
using Mediana.Codecs.Vp9;

namespace Mediana.Codecs.Vp9.Tests
{
    public class Vp9HeaderTests
    {
        [Theory]
        [InlineData("main8", 0, 8, true, true)]
        [InlineData("main10", 2, 10, true, true)]
        [InlineData("high8", 1, 8, false, false)]
        [InlineData("high12", 3, 12, false, false)]
        [InlineData("lossless", 0, 8, true, true)]
        [InlineData("resilient", 0, 8, true, true)]
        [InlineData("tiles", 0, 8, true, true)]
        [InlineData("altref", 0, 8, true, true)]
        public async Task IndependentBitstreamsRetainProfilePrecisionGeometryAndReferenceUpdates(string name, int profile, int depth, bool sx, bool sy)
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data");
            byte[] ivf = await File.ReadAllBytesAsync(Path.Combine(root, name + ".ivf"));
            using JsonDocument expected = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, name + ".json")));
            JsonElement frames = expected.RootElement.GetProperty("frames");
            Vp9HeaderState state = new();
            Range[] ranges = new Range[8]; int cursor = 32, visible = 0, hidden = 0, superframes = 0;
            while (cursor < ivf.Length)
            {
                int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(ivf.AsSpan(cursor))); cursor += 12;
                ReadOnlyMemory<byte> packet = ivf.AsMemory(cursor, length); cursor += length;
                int count = Vp9Superframe.Parse(packet.Span, ranges);
                if (count > 1) { superframes++; }
                for (int i = 0; i < count; i++)
                {
                    Vp9FrameHeader header = Vp9FrameHeader.Parse(packet.Span[ranges[i]], state);
                    Assert.Equal(profile, header.Geometry.Profile); Assert.Equal(depth, header.Geometry.BitDepth);
                    Assert.Equal(sx, header.Geometry.SubsamplingX); Assert.Equal(sy, header.Geometry.SubsamplingY);
                    if (name == "lossless") { Assert.True(header.Lossless); }
                    if (name == "resilient") { Assert.True(header.ErrorResilient); Assert.True(header.ParallelDecoding); }
                    if (name == "tiles") { Assert.Equal(1, header.TileColumnsLog2); Assert.Equal(1, header.TileRowsLog2); }
                    if (header.ShowFrame)
                    {
                        JsonElement reference = frames[visible++];
                        Assert.Equal(reference.GetProperty("width").GetInt32(), header.Geometry.Width);
                        Assert.Equal(reference.GetProperty("height").GetInt32(), header.Geometry.Height);
                        Assert.Equal(reference.GetProperty("key_frame").GetInt32() == 1, header.KeyFrame);
                    }
                    else { hidden++; }
                    state.Commit(header);
                }
            }
            Assert.Equal(frames.GetArrayLength(), visible);
            if (name == "altref") { Assert.True(hidden > 0); Assert.True(superframes > 0); }
        }

        [Fact]
        public async Task ShowExistingReferencesRequireInitializedMatchingProfileAndZeroPadding()
        {
            byte[] ivf = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "main8.ivf"));
            int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(ivf.AsSpan(32)));
            Vp9HeaderState state = new();
            Assert.Throws<InvalidDataException>(() => Vp9FrameHeader.Parse([0x88], state));
            state.Commit(Vp9FrameHeader.Parse(ivf.AsSpan(44, length), state));
            for (int i = 0; i < 8; i++)
            {
                Vp9FrameHeader shown = Vp9FrameHeader.Parse([(byte)(0x88 | i), 0, 0], state);
                Assert.True(shown.ShowExisting); Assert.True(shown.ShowFrame); Assert.Equal(i, shown.ExistingReference);
                Assert.Equal(state.References[i], shown.Geometry); Assert.Equal(0, shown.RefreshReferences);
            }
            Assert.Throws<InvalidDataException>(() => Vp9FrameHeader.Parse([0x88, 1], state));
            Assert.Throws<InvalidDataException>(() => Vp9FrameHeader.Parse([0xa8], state));
            Assert.Throws<InvalidDataException>(() => Vp9FrameHeader.Parse([0x70], state));
        }

        [Fact]
        public async Task EveryTruncatedOpeningHeaderIsRejectedBeforeReferenceCommit()
        {
            byte[] ivf = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "main8.ivf"));
            int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(ivf.AsSpan(32)));
            Vp9FrameHeader complete = Vp9FrameHeader.Parse(ivf.AsSpan(44, length), new());
            for (int size = 0; size < complete.CompressedOffset + complete.CompressedLength + 1; size++)
            {
                byte[] prefix = ivf.AsSpan(44, size).ToArray(); Vp9HeaderState state = new();
                Assert.Throws<InvalidDataException>(() => Vp9FrameHeader.Parse(prefix, state));
                Assert.All(state.References, reference => Assert.Null(reference));
            }
        }
    }
}

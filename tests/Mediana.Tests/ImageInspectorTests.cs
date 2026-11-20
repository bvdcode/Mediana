// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Codecs;
using Mediana.Core.Metadata;
using Mediana.Images.Metadata;
using Mediana.IO;

namespace Mediana.Tests
{
    public class ImageInspectorTests
    {
        [Fact]
        public async Task ProbeWithoutPixelDecodersPreservesRandomAccessPositionAndOwnsMetadata()
        {
            byte[] data = Pcx();
            MediaEngine engine = new MediaEngineBuilder().AddImageInspector(new PcxMetadataReader()).Build();
            Assert.Empty(engine.ImageDecoderImplementations);
            Assert.Equal("pcx.managed.metadata", Assert.Single(engine.ImageInspectorImplementations));
            ImageInfo image;
            using (MemoryStream stream = new(data))
            {
                await using RandomAccessStreamByteSource source = new(stream, leaveOpen: true);
                await source.ReadAsync(new byte[7]);
                image = (await engine.ProbeAsync(source)).Image!;
                Assert.Equal(7, stream.Position);
                Assert.True(stream.CanRead);
            }
            data.AsSpan().Clear();
            Assert.Equal("pcx", image.FormatId);
            Assert.Equal(17, image.Width); Assert.Equal(9, image.Height);
            Assert.Equal(8L, image.Metadata.Properties.Single(item => item.Name == "bits_per_plane").Values[0].AsInteger());
        }

        [Fact]
        public async Task SequentialProbeAndBorrowedMemoryInspectionUseTheSameReader()
        {
            MediaEngine engine = new MediaEngineBuilder().AddImageInspector(new PcxMetadataReader()).Build();
            byte[] data = Pcx();
            await using ShortReadSource source = new(data);
            ImageInfo sequential = (await engine.ProbeAsync(source)).Image!;
            ImageInfo borrowed = await engine.InspectImageAsync(data);
            Assert.Equal(sequential.FormatId, borrowed.FormatId);
            Assert.Equal(sequential.Width, borrowed.Width);
            Assert.False(source.IsDisposed);
            Assert.Empty(await engine.ReadImagePreviewsAsync(data));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await engine.InspectImageAsync(data, new(true)));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await engine.ReadImagePreviewsAsync(data, new(true)));
            await Assert.ThrowsAsync<NotSupportedException>(async () => await engine.DecodeImageAsync(data, new(1024)));
        }

        [Fact]
        public async Task InspectorSnapshotAndRegistrationValidationAreIndependentOfDecoderRegistry()
        {
            MediaEngineBuilder builder = new();
            MediaEngine empty = builder.Build();
            builder.AddImageInspector(new PcxMetadataReader());
            MediaEngine filled = builder.Build();
            Assert.Throws<ArgumentException>(() => builder.AddImageInspector(new PcxMetadataReader()));
            await Assert.ThrowsAsync<NotSupportedException>(async () => await empty.InspectImageAsync(Pcx()));
            Assert.Equal("pcx", (await filled.InspectImageAsync(Pcx())).FormatId);
            await using ShortReadSource source = new(Pcx());
            await Assert.ThrowsAsync<NotSupportedException>(async () => await filled.ProbeAsync(source, new() { MaxProbeBytes = 73 }));
            Assert.False(source.IsDisposed);
        }

        private static byte[] Pcx()
        {
            byte[] data = new byte[128];
            data[0] = 10; data[1] = 5; data[2] = 1; data[3] = 8; data[65] = 3;
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), 16);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(10), 8);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(66), 18);
            return data;
        }
    }
}

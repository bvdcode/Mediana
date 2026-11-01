// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Security.Cryptography;
using Mediana.Containers;
using Mediana.Containers.Matroska;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.IO;
using static Mediana.Tests.MatroskaMetadataTestData;

namespace Mediana.Tests
{
    internal static class MatroskaAttachmentReferenceInput
    {
        public static async Task VerifyAsync()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data");
            byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(root, "Matroska", "avc-aac-attachment.mkv"));
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, "Metadata", "cover.png"));
            Require(Convert.ToHexStringLower(SHA256.HashData(encoded)) == "63f999ab20fc744304bbcb45b5287eb50a94420df5f6b816f2ef19896e5de87c");
            Require(expected.Length == 1236 && Convert.ToHexStringLower(SHA256.HashData(expected)) ==
                "3d20a35d9ff0be4e351b4545cb78c74647b095c04f2192db029d24092609ce9e");
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new MatroskaReaderFactory()).Build();
            using MemoryStream input = new(encoded, writable: false);
            await using RandomAccessStreamByteSource source = new(input, leaveOpen: true);
            await source.ReadAsync(new byte[3]);
            await using ContainerSession session = await engine.OpenContainerAsync(source,
                new() { BufferBudgetBytes = 131072, MaxPacketReads = 85 });
            IReadOnlyList<ContainerAttachmentInfo> declarations = session.Attachments!;
            Require(declarations.Count == 1 && session.Pictures is null && session.Streams.Count == 2);
            MatroskaAttachmentInfo info = (MatroskaAttachmentInfo)declarations[0];
            Require(info.Uid == 13717314701664214952 && info.FileName == "cover.png" && info.MediaType == "image/png" &&
                info.Description is null && info.ByteLength == expected.Length);
            long live = session.AllocatedBufferBytes;
            using MediaBuffer retained = await session.ReadAttachmentAsync(0);
            Require(retained.Memory.Span.SequenceEqual(expected) && session.AllocatedBufferBytes == live + expected.Length);
            using (MediaBuffer repeated = await session.ReadAttachmentAsync(0))
            {
                Require(repeated.Memory.Span.SequenceEqual(expected) && session.AllocatedBufferBytes == live + 2 * expected.Length);
            }
            int packetCount = 0;
            while (await session.ReadPacketAsync() is MediaPacket packet)
            {
                packet.Dispose();
                packetCount++;
            }
            Require(packetCount == 84 && input.Position == 3);
            await session.DisposeAsync();
            encoded.AsSpan().Clear();
            Require(session.AllocatedBufferBytes == expected.Length && retained.Memory.Span.SequenceEqual(expected));
            retained.Dispose();
            Require(session.AllocatedBufferBytes == 0 && input.CanRead);
            await using EbmlElementTestSource noAttachments = new(await File.ReadAllBytesAsync(Path.Combine(root, "Matroska", "mp3-low-rate.mka")));
            await using ContainerSession empty = await engine.OpenContainerAsync(noAttachments);
            Require(empty.Attachments!.Count == 0 && empty.Pictures is null);
            await RejectAsync<ArgumentOutOfRangeException>(async () => await empty.ReadAttachmentAsync(0));
            using MediaPacket first = (await empty.ReadPacketAsync())!;
            Require(first.Data.Length > 0);
            await empty.DisposeAsync();
            first.Dispose();
            Require(empty.AllocatedBufferBytes == 0 && !noAttachments.IsDisposed);
        }
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Containers.Flac;
using Mediana.Containers.Mp3;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class ContainerPictureTestInput
    {
        public static MediaEngine CreateEngine() => new MediaEngineBuilder()
            .AddContainer(new Mp3ReaderFactory())
            .AddContainer(new FlacReaderFactory())
            .AddContainer(new Mp4ReaderFactory()).Build();

        public static async Task VerifyAsync(string root)
        {
            (string Folder, string File, string[] Covers)[] cases =
            [
                ("Mp3", "stereo-vbr-cover.mp3", ["cover.png"]),
                ("Mp3", "stereo-cbr-id3v23.mp3", []),
                ("Flac", "stereo-covers.flac", ["cover.png", "cover.jpg"]),
                ("Flac", "mono-speech.flac", []),
                ("Metadata", "covers.m4a", ["cover.png", "cover.jpg"]),
                ("Metadata", "mdta.mp4", [])
            ];
            foreach ((string folder, string file, string[] covers) in cases)
            {
                await VerifyFileAsync(root, folder, file, covers);
            }
        }

        private static async Task VerifyFileAsync(string root, string folder, string file, string[] covers)
        {
            const int BudgetBytes = 128 * 1024;
            string path = Path.Combine(root, folder, file);
            MediaEngine engine = CreateEngine();
            await using FileStream input = File.OpenRead(path);
            await using RandomAccessStreamByteSource source = new(input, leaveOpen: true);
            await source.ReadAsync(new byte[3]);
            await using ContainerSession session = await engine.OpenContainerAsync(source,
                new MediaOpenOptions { BufferBudgetBytes = BudgetBytes, MaxPacketReads = 2 });
            await using RandomAccessStreamByteSource baselineSource = new(File.OpenRead(path));
            await using ContainerSession baseline = await engine.OpenContainerAsync(baselineSource);
            IReadOnlyList<ContainerPictureInfo> declarations = session.Pictures
                ?? throw new InvalidDataException("The container does not expose its supported picture capability.");
            if (declarations.Count != covers.Length || !ReferenceEquals(session.Pictures, declarations))
            {
                throw new InvalidDataException("The picture count or immutable declaration list changed.");
            }
            await VerifyPacketAsync(session, baseline);
            long allocatedBytes = session.AllocatedBufferBytes;
            List<(MediaBuffer Buffer, byte[] Expected)> retained = [];
            try
            {
                for (int index = covers.Length - 1; index >= 0; index--)
                {
                    byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, "Metadata", covers[index]));
                    MediaBuffer picture = await session.ReadPictureAsync(index);
                    retained.Add((picture, expected));
                    string contentType = Path.GetExtension(covers[index]) switch
                    {
                        ".png" => "image/png",
                        ".jpg" => "image/jpeg",
                        _ => throw new InvalidDataException("Unexpected picture reference.")
                    };
                    ContainerPictureInfo info = declarations[index];
                    if (!picture.Memory.Span.SequenceEqual(expected) || info.ByteLength != expected.Length
                        || info.MediaType != contentType || info.DataKind != PictureDataKind.Embedded)
                    {
                        throw new InvalidDataException("Picture bytes or declarations differ from the original image.");
                    }
                    using MediaBuffer repeated = await session.ReadPictureAsync(index);
                    if (!repeated.Memory.Span.SequenceEqual(expected) || session.AllocatedBufferBytes != allocatedBytes)
                    {
                        throw new InvalidOperationException("Picture extraction copied or changed its backing allocation.");
                    }
                }
                await VerifyPacketAsync(session, baseline);
                if (input.Position != 3 || session.AllocatedBufferBytes > BudgetBytes)
                {
                    throw new InvalidOperationException("Picture extraction changed the borrowed cursor or memory budget.");
                }
                await session.DisposeAsync();
                foreach ((MediaBuffer buffer, byte[] expected) in retained)
                {
                    if (!buffer.Memory.Span.SequenceEqual(expected) || session.AllocatedBufferBytes == 0)
                    {
                        throw new InvalidOperationException("An extracted picture lost its backing allocation after disposal.");
                    }
                }
            }
            finally
            {
                foreach ((MediaBuffer buffer, _) in retained)
                {
                    buffer.Dispose();
                }
            }
            if (session.AllocatedBufferBytes != 0 || !input.CanRead || input.Position != 3)
            {
                throw new InvalidOperationException("Picture extraction retained buffers or changed source ownership.");
            }
        }

        private static async Task VerifyPacketAsync(ContainerSession session, ContainerSession baseline)
        {
            using MediaPacket actual = await session.ReadPacketAsync()
                ?? throw new InvalidDataException("A media packet is missing after picture extraction.");
            using MediaPacket expected = await baseline.ReadPacketAsync()
                ?? throw new InvalidDataException("The reference media packet is missing.");
            if (!actual.Data.Span.SequenceEqual(expected.Data.Span) || actual.StreamIndex != expected.StreamIndex
                || actual.PresentationTime != expected.PresentationTime || actual.DecodeTime != expected.DecodeTime
                || actual.Duration != expected.Duration || actual.IsKeyFrame != expected.IsKeyFrame)
            {
                throw new InvalidDataException("Picture extraction changed the packet cursor, payload or timestamps.");
            }
        }
    }
}

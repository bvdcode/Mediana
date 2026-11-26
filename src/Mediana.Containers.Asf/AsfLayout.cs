// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.IO;

namespace Mediana.Containers.Asf
{
    internal class AsfLayout(IRandomAccessByteSource source, MediaBufferAllocator allocator)
    {
        private readonly byte[] header = new byte[AsfObject.HeaderLength];
        private readonly Dictionary<int, AsfMediaObject> pending = [];
        private AsfFileProperties? properties;

        public Dictionary<int, AsfStream> Streams { get; } = [];
        public List<MediaTag> Tags { get; } = [];
        public List<string> Languages { get; } = [];
        public Dictionary<int, AsfStreamDescription> Descriptions { get; } = [];
        public List<AsfMediaObject> Packets { get; } = [];
        public AsfFileProperties Properties => properties ?? throw new InvalidDataException("The ASF File Properties object is missing.");

        public async ValueTask ReadAsync(CancellationToken token)
        {
            long length = source.Length!.Value;
            AsfObject root = await ReadObjectAsync(0, length, token).ConfigureAwait(false);
            if (root.Id != AsfObjects.Header || root.PayloadLength < 6)
            {
                throw new InvalidDataException("The ASF Header object is missing.");
            }
            await source.ReadExactlyAtAsync(root.PayloadStart, header.AsMemory(0, 6), token).ConfigureAwait(false);
            uint count = BinaryPrimitives.ReadUInt32LittleEndian(header);
            long position = root.PayloadStart + 6;
            for (uint index = 0; index < count; index++)
            {
                AsfObject entry = await ReadObjectAsync(position, root.End, token).ConfigureAwait(false);
                await ReadHeaderObjectAsync(entry, token).ConfigureAwait(false);
                position = entry.End;
            }
            if (position != root.End || Streams.Count == 0)
            {
                throw new InvalidDataException("The ASF Header extent or stream declarations are invalid.");
            }
            for (position = root.End; position < length;)
            {
                AsfObject entry = await ReadObjectAsync(position, length, token).ConfigureAwait(false);
                if (entry.Id == AsfObjects.Data)
                {
                    await ReadDataAsync(entry, token).ConfigureAwait(false);
                    return;
                }
                position = entry.End;
            }
            throw new InvalidDataException("The ASF Data object is missing.");
        }

        private async ValueTask<AsfObject> ReadObjectAsync(long offset, long end, CancellationToken token)
        {
            if (end - offset < header.Length) { throw new InvalidDataException("Truncated ASF object header."); }
            await source.ReadExactlyAtAsync(offset, header, token).ConfigureAwait(false);
            return AsfObject.Read(header, offset, end);
        }

        private async ValueTask ReadHeaderObjectAsync(AsfObject entry, CancellationToken token)
        {
            Guid id = entry.Id;
            if (id == AsfObjects.HeaderExtension)
            {
                await ReadExtensionAsync(entry, token).ConfigureAwait(false);
                return;
            }
            if (id != AsfObjects.FileProperties && id != AsfObjects.StreamProperties &&
                id != AsfObjects.ContentDescription && id != AsfObjects.ExtendedContentDescription)
            {
                return;
            }

            if (entry.PayloadLength is 0 or > int.MaxValue) { throw new InvalidDataException("Invalid ASF header object size."); }
            using WritableMediaBuffer data = allocator.Allocate((int)entry.PayloadLength);
            await source.ReadExactlyAtAsync(entry.PayloadStart, data.Memory, token).ConfigureAwait(false);
            if (id == AsfObjects.FileProperties)
            {
                if (properties is not null) { throw new InvalidDataException("Duplicate ASF File Properties object."); }
                properties = AsfFileProperties.Read(data.Span);
            }
            else if (id == AsfObjects.StreamProperties)
            {
                AsfStream? stream = AsfStream.Read(data.Span, Streams.Count);
                if (stream is not null && !Streams.TryAdd(stream.Number, stream))
                {
                    throw new InvalidDataException("Duplicate ASF stream number.");
                }
            }
            else if (id == AsfObjects.ContentDescription)
            {
                AsfTags.ReadContent(data.Span, Tags);
            }
            else
            {
                AsfTags.ReadExtended(data.Span, Tags);
            }
        }

        private async ValueTask ReadExtensionAsync(AsfObject entry, CancellationToken token)
        {
            const int extensionHeaderLength = 22;
            if (entry.PayloadLength < extensionHeaderLength)
            {
                throw new InvalidDataException("Truncated ASF Header Extension object.");
            }
            await source.ReadExactlyAtAsync(entry.PayloadStart, header.AsMemory(0, extensionHeaderLength), token).ConfigureAwait(false);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(18));
            if (size != entry.PayloadLength - extensionHeaderLength)
            {
                throw new InvalidDataException("Invalid ASF Header Extension data length.");
            }
            for (long position = entry.PayloadStart + extensionHeaderLength; position < entry.End;)
            {
                AsfObject nested = await ReadObjectAsync(position, entry.End, token).ConfigureAwait(false);
                if (nested.Id == AsfObjects.ExtendedStreamProperties || nested.Id == AsfObjects.LanguageList)
                {
                    if (nested.PayloadLength is 0 or > int.MaxValue)
                    {
                        throw new InvalidDataException("Invalid ASF extended stream metadata length.");
                    }
                    using WritableMediaBuffer data = allocator.Allocate((int)nested.PayloadLength);
                    await source.ReadExactlyAtAsync(nested.PayloadStart, data.Memory, token).ConfigureAwait(false);
                    if (nested.Id == AsfObjects.ExtendedStreamProperties)
                    {
                        AsfStreamDescription description = AsfStreamDescription.Read(data.Span);
                        if (!Descriptions.TryAdd(description.Number, description))
                        {
                            throw new InvalidDataException("Duplicate ASF Extended Stream Properties object.");
                        }
                    }
                    else
                    {
                        ReadLanguages(data.Span);
                    }
                }
                position = nested.End;
            }
        }

        private void ReadLanguages(ReadOnlySpan<byte> data)
        {
            if (data.Length < 2 || Languages.Count != 0)
            {
                throw new InvalidDataException("Truncated or duplicate ASF Language List object.");
            }
            int count = BinaryPrimitives.ReadUInt16LittleEndian(data);
            int offset = 2;
            for (int index = 0; index < count; index++)
            {
                if (offset == data.Length) { throw new InvalidDataException("Truncated ASF language identifier."); }
                int length = data[offset++];
                if (length < 2 || (length & 1) != 0 || length > data.Length - offset)
                {
                    throw new InvalidDataException("Invalid ASF language identifier length.");
                }
                Languages.Add(Encoding.Unicode.GetString(data.Slice(offset, length)).TrimEnd('\0'));
                offset += length;
            }
            if (offset != data.Length) { throw new InvalidDataException("Unexpected bytes after ASF language identifiers."); }
        }

        private async ValueTask ReadDataAsync(AsfObject entry, CancellationToken token)
        {
            const int dataHeaderLength = 26;
            if (entry.PayloadLength < dataHeaderLength) { throw new InvalidDataException("Truncated ASF Data header."); }
            AsfFileProperties file = Properties;
            byte[] identity = new byte[dataHeaderLength];
            await source.ReadExactlyAtAsync(entry.PayloadStart, identity, token).ConfigureAwait(false);
            ulong packetCount = BinaryPrimitives.ReadUInt64LittleEndian(identity.AsSpan(16));
            if (new Guid(identity.AsSpan(0, 16)) != file.Id || packetCount != file.PacketCount)
            {
                throw new InvalidDataException("The ASF Data and File Properties declarations disagree.");
            }
            using WritableMediaBuffer buffer = allocator.Allocate(file.PacketMaximum);
            long position = entry.PayloadStart + dataHeaderLength;
            Action<AsfPayload> receive = Accept;
            for (ulong index = 0; index < packetCount; index++)
            {
                int count = (int)Math.Min(file.PacketMaximum, entry.End - position);
                if (count < file.PacketMinimum) { throw new InvalidDataException("ASF transport packets exceed their Data object."); }
                await source.ReadExactlyAtAsync(position, buffer.Memory[..count], token).ConfigureAwait(false);
                position += AsfPacketParser.Parse(buffer.Span[..count], position, file.PacketMinimum, file.PacketMaximum, receive);
            }
            if (position != entry.End) { throw new InvalidDataException("Unaccounted bytes at the end of the ASF Data object."); }
            foreach ((int number, AsfMediaObject packet) in pending)
            {
                Finish(packet, Streams[number]);
            }
            pending.Clear();
        }

        private static void Finish(AsfMediaObject packet, AsfStream stream)
        {
            packet.ValidateComplete();
            if (packet.Length != 0) { stream.Count++; }
        }

        private void Accept(AsfPayload part)
        {
            if (!Streams.TryGetValue(part.Stream, out AsfStream? stream)) { return; }
            if (part.Offset == 0)
            {
                if (pending.Remove(part.Stream, out AsfMediaObject? previous))
                {
                    Finish(previous, stream);
                }
                AsfMediaObject next = new(stream.Stream.StreamIndex, part);
                pending.Add(part.Stream, next);
                Packets.Add(next);
            }
            if (!pending.TryGetValue(part.Stream, out AsfMediaObject? packet))
            {
                throw new InvalidDataException("An ASF media object starts with a continuation fragment.");
            }
            packet.Add(part);
            stream.Bytes = checked(stream.Bytes + part.Length);
        }
    }
}

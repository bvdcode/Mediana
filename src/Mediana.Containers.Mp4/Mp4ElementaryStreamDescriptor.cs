// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Mp4
{
    internal static class Mp4ElementaryStreamDescriptor
    {
        public static (string CodecId, ReadOnlyMemory<byte> Configuration) ReadAudioConfiguration(ReadOnlyMemory<byte> data)
        {
            Mp4Binary.FullBox(data.Span);
            ReadOnlyMemory<byte> elementary = Find(data[4..], 3);
            Mp4Binary.Range(elementary.Span, 0, 3);
            int offset = 3;
            byte flags = elementary.Span[2];
            if ((flags & 0x80) != 0)
            {
                offset += 2;
            }
            if ((flags & 0x40) != 0)
            {
                offset += 1 + Mp4Binary.Range(elementary.Span, offset, 1)[0];
            }
            if ((flags & 0x20) != 0)
            {
                offset += 2;
            }
            Mp4Binary.Range(elementary.Span, 0, offset);
            ReadOnlyMemory<byte> decoder = Find(elementary[offset..], 4);
            Mp4Binary.Range(decoder.Span, 0, 13);
            if ((decoder.Span[1] >> 2) != 5)
            {
                throw new NotSupportedException("The MP4 elementary stream descriptor is not audio.");
            }
            switch (decoder.Span[0])
            {
                case 0x69:
                case 0x6b:
                    ReadOnlyMemory<byte>? specific = Find(decoder[13..], 5, required: false);
                    if (specific.HasValue && !specific.Value.IsEmpty)
                    {
                        throw new NotSupportedException("MP4 MPEG audio decoder-specific configuration is not supported.");
                    }
                    return ("mp3", ReadOnlyMemory<byte>.Empty);
                case 0x40:
                    break;
                default:
                    throw new NotSupportedException("The MP4 audio object type indication is not supported.");
            }
            ReadOnlyMemory<byte> configuration = Find(decoder[13..], 5);
            if (configuration.Length < 2)
            {
                throw new InvalidDataException("The MP4 audio configuration is truncated.");
            }
            int objectType = configuration.Span[0] >> 3;
            if (objectType is not (2 or 5 or 29))
            {
                throw new NotSupportedException($"MPEG-4 audio object type {objectType} is not supported.");
            }
            return ("aac", configuration);
        }

        private static ReadOnlyMemory<byte> Find(ReadOnlyMemory<byte> data, byte expectedTag) =>
            Find(data, expectedTag, required: true)!.Value;

        private static ReadOnlyMemory<byte>? Find(ReadOnlyMemory<byte> data, byte expectedTag, bool required)
        {
            ReadOnlyMemory<byte>? found = null;
            int offset = 0;
            while (offset < data.Length)
            {
                byte tag = data.Span[offset++];
                int length = 0;
                int lengthBytes = 0;
                byte next;
                do
                {
                    next = Mp4Binary.Range(data.Span, offset++, 1)[0];
                    length = (length << 7) | (next & 0x7F);
                    lengthBytes++;
                    if (lengthBytes == 4 && (next & 0x80) != 0)
                    {
                        throw new InvalidDataException("The MP4 descriptor length is invalid.");
                    }
                }
                while ((next & 0x80) != 0);
                Mp4Binary.Range(data.Span, offset, length);
                if (tag == expectedTag)
                {
                    if (found is not null)
                    {
                        throw new InvalidDataException("An MP4 elementary stream descriptor is duplicated.");
                    }
                    found = data.Slice(offset, length);
                }
                offset += length;
            }
            if (required && found is null)
            {
                throw new InvalidDataException($"The required MP4 descriptor {expectedTag} is missing.");
            }
            return found;
        }
    }
}

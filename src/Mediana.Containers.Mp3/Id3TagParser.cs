// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text;

namespace Mediana.Containers.Mp3
{
    internal static class Id3TagParser
    {
        public static Id3TagData Read(Span<byte> data, int version, int flags, Mp3ReadOptions options,
            CancellationToken cancellationToken)
        {
            if (version == 3 && (flags & 0x80) != 0)
            {
                data = data[..Id3Binary.RemoveUnsynchronisation(data, cancellationToken)];
            }
            int position = ReadExtendedHeader(data, version, flags, out uint? declaredPadding);
            List<Id3TextFrame> text = [];
            List<Id3Picture> pictures = [];
            HashSet<string> textIds = new(StringComparer.Ordinal);
            HashSet<string> descriptions = new(StringComparer.Ordinal);
            int frames = 0;
            int padding = 0;
            while (position < data.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (data[position] == 0)
                {
                    if (data[position..].ContainsAnyExcept((byte)0) || (version == 4 && (flags & 0x10) != 0))
                    {
                        throw new InvalidDataException("The ID3 padding is invalid or accompanies a footer.");
                    }
                    padding = data.Length - position;
                    break;
                }
                if (frames++ == options.MaxMetadataFrames)
                {
                    throw new NotSupportedException("The ID3 frame count exceeds its configured limit.");
                }
                if (data.Length - position < 10)
                {
                    throw new InvalidDataException("The ID3 frame header is truncated.");
                }
                Span<byte> header = data.Slice(position, 10);
                foreach (byte value in header[..4])
                {
                    if (value is not (>= (byte)'A' and <= (byte)'Z') and not (>= (byte)'0' and <= (byte)'9'))
                    {
                        throw new InvalidDataException("An ID3 frame identifier must contain four uppercase ASCII letters or digits.");
                    }
                }
                string id = Encoding.ASCII.GetString(header[..4]);
                uint wireLength = version switch
                {
                    3 => BinaryPrimitives.ReadUInt32BigEndian(header[4..]),
                    4 => (uint)Id3Binary.ReadSynchsafe(header[4..]),
                    _ => throw new InvalidOperationException()
                };
                if (wireLength == 0 || wireLength > data.Length - position - 10)
                {
                    throw new InvalidDataException("The ID3 frame size is zero or exceeds the tag.");
                }
                int frameFlags = header[8] << 8 | header[9];
                int wireOffset = position + 10;
                (int offset, int length) = PrepareFrame(data.Slice(wireOffset, (int)wireLength), version, flags, frameFlags, cancellationToken);
                offset += wireOffset;
                ReadOnlySpan<byte> body = data.Slice(offset, length);
                if (id[0] == 'T')
                {
                    Id3TextFrame frame = Id3FrameParser.ReadText(id, body, version, options);
                    string key = id == "TXXX" ? id + ":" + frame.Description : id;
                    if (!textIds.Add(key))
                    {
                        throw new InvalidDataException("The ID3 tag contains a duplicate text frame or user-text description.");
                    }
                    text.Add(frame);
                }
                else if (id == "APIC")
                {
                    if (pictures.Count == options.MaxPictures)
                    {
                        throw new NotSupportedException("The ID3 picture count exceeds its configured limit.");
                    }
                    Id3Picture picture = Id3FrameParser.ReadPicture(body, offset, version, options);
                    if (!descriptions.Add(picture.Info.Description)
                        || (picture.Info.PictureType is 1 or 2 && pictures.Any(value => value.Info.PictureType == picture.Info.PictureType)))
                    {
                        throw new InvalidDataException("The ID3 tag contains duplicate picture descriptions or icons.");
                    }
                    pictures.Add(picture);
                }
                position = wireOffset + (int)wireLength;
            }
            if (frames == 0 || (declaredPadding is uint expected && expected != padding))
            {
                throw new InvalidDataException("The ID3 tag contains no frames or disagrees with its declared padding size.");
            }
            return new Id3TagData(text.AsReadOnly(), pictures.AsReadOnly());
        }

        private static (int Offset, int Length) PrepareFrame(Span<byte> body, int version, int tagFlags, int flags,
            CancellationToken cancellationToken)
        {
            int offset = 0;
            switch (version)
            {
                case 3:
                    if ((flags & ~0xe0e0) != 0)
                    {
                        throw new InvalidDataException("An ID3v2.3 frame uses reserved flags.");
                    }
                    if ((flags & 0xc0) != 0)
                    {
                        throw new NotSupportedException("Compressed or encrypted ID3 frames are not supported.");
                    }
                    if ((flags & 0x20) != 0)
                    {
                        offset++;
                    }
                    break;
                case 4:
                    if ((flags & ~0x704f) != 0 || ((tagFlags & 0x80) != 0 && (flags & 2) == 0))
                    {
                        throw new InvalidDataException("An ID3v2.4 frame uses reserved or inconsistent flags.");
                    }
                    if ((flags & 0x0c) != 0)
                    {
                        throw new NotSupportedException("Compressed or encrypted ID3 frames are not supported.");
                    }
                    if ((flags & 2) != 0)
                    {
                        body = body[..Id3Binary.RemoveUnsynchronisation(body, cancellationToken)];
                    }
                    if ((flags & 0x40) != 0)
                    {
                        offset++;
                    }
                    if ((flags & 1) != 0)
                    {
                        if (body.Length - offset < 4)
                        {
                            throw new InvalidDataException("The ID3 data length indicator is truncated.");
                        }
                        int declared = Id3Binary.ReadSynchsafe(body[offset..]);
                        offset += 4;
                        if (declared != body.Length - offset)
                        {
                            throw new InvalidDataException("The ID3 data length indicator does not match its decoded payload.");
                        }
                    }
                    break;
                default:
                    throw new InvalidOperationException();
            }
            if (offset > body.Length)
            {
                throw new InvalidDataException("The ID3 group identifier is truncated.");
            }
            return (offset, body.Length - offset);
        }

        private static int ReadExtendedHeader(ReadOnlySpan<byte> data, int version, int flags, out uint? padding)
        {
            padding = null;
            if ((flags & 0x40) == 0)
            {
                return 0;
            }
            if (data.Length < 6)
            {
                throw new InvalidDataException("The ID3 extended header is truncated.");
            }
            switch (version)
            {
                case 3:
                    uint length = BinaryPrimitives.ReadUInt32BigEndian(data);
                    if (length > data.Length - 4 || length < 6)
                    {
                        throw new InvalidDataException("The ID3v2.3 extended header length is invalid.");
                    }
                    if (data[4] != 0 || data[5] != 0 || length != 6)
                    {
                        throw new NotSupportedException("ID3v2.3 extended CRC fields are not supported.");
                    }
                    padding = BinaryPrimitives.ReadUInt32BigEndian(data[6..]);
                    return (int)length + 4;
                case 4:
                    int size = Id3Binary.ReadSynchsafe(data);
                    if (size < 6 || size > data.Length || data[4] != 1)
                    {
                        throw new InvalidDataException("The ID3v2.4 extended header size or flag-byte count is invalid.");
                    }
                    if (size != 6 || data[5] != 0)
                    {
                        throw new NotSupportedException("ID3v2.4 update, extended CRC and restriction fields are not supported.");
                    }
                    return size;
                default:
                    throw new InvalidOperationException();
            }
        }
    }
}

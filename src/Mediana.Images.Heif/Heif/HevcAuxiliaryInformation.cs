// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.IO;
using Mediana.Images.Heif.Binary;
using Mediana.Images.Heif.Hevc;

namespace Mediana.Images.Heif.Container
{
    internal static class HevcAuxiliaryInformation
    {
        private const int AlphaChannelPayloadType = 165;

        public static AlphaChannelInformation? Read(ReadOnlyMemory<byte> subtype, int lengthSize)
        {
            if (subtype.IsEmpty)
            {
                return null;
            }
            ByteReader reader = new ByteReader(subtype);
            uint length = reader.U32();
            if (length != reader.Remaining)
            {
                throw new InvalidDataException("Auxiliary SEI length exceeds its property.");
            }
            AlphaChannelInformation? information = null;
            foreach (NalPayload nal in NalPayload.ReadLengthPrefixed(reader.Bytes(reader.Remaining), lengthSize))
            {
                if ((nal.Type != 39) && (nal.Type != 40))
                {
                    throw new InvalidDataException("Auxiliary configuration contains a non-SEI NAL unit.");
                }
                ArraySegment<byte> messageData = nal.Bytes.Slice(2);
                ByteReader messages = new ByteReader(messageData.AsMemory());
                while (messages.Remaining > 0)
                {
                    int type = ReadExtended(messages);
                    if ((type == 128) && (messages.Remaining == 0))
                    {
                        break;
                    }
                    int size = ReadExtended(messages);
                    int payloadOffset = messages.Position;
                    messages.Skip(size);
                    if (type == AlphaChannelPayloadType)
                    {
                        information = AlphaChannelInformation.Read(messageData.Slice(payloadOffset, size));
                    }
                }
            }
            return information;
        }

        private static int ReadExtended(ByteReader reader)
        {
            int value = 0;
            byte part;
            do
            {
                part = reader.U8();
                if (value > (int.MaxValue - part))
                {
                    throw new InvalidDataException("Auxiliary SEI field exceeds the supported range.");
                }
                value += part;
            }
            while (part == 255);
            return value;
        }
    }
}

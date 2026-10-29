// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Images.Heif.Binary;
using System;
using System.Collections.Generic;
using System.IO;

namespace Mediana.Images.Heif.Hevc
{
    internal class NalPayload
    {
        private readonly ReadOnlyMemory<byte> encoded;

        public NalPayload(ReadOnlyMemory<byte> encoded)
        {
            if (encoded.Length < 2)
            {
                throw new InvalidDataException("Truncated NAL header.");
            }
            int header = (encoded.Span[0] << 8) | encoded.Span[1];
            if (((header & 0x8000) != 0) || ((header & 7) == 0))
            {
                throw new InvalidDataException("Invalid NAL header.");
            }

            if (((header >> 3) & 63) != 0)
            {
                throw new UnsupportedHeicFeatureException("multilayer HEVC");
            }
            this.encoded = encoded;
            Type = (header >> 9) & 63;
            byte[] rbsp = new byte[encoded.Length];
            int target = 0;
            int zeros = 0;
            for (int i = 0; i < encoded.Length; i++)
            {
                byte value = encoded.Span[i];
                if ((i >= 2) && (zeros >= 2) && (value == 3))
                {
                    if (((i + 1) == encoded.Length) || (encoded.Span[i + 1] > 3))
                    {
                        throw new InvalidDataException("Invalid emulation-prevention byte.");
                    }

                    zeros = 0;
                    continue;
                }

                rbsp[target++] = value;
                zeros = value == 0 ? zeros + 1 : 0;
            }

            Bytes = new ArraySegment<byte>(rbsp, 0, target);
        }

        public int Type { get; }
        public ArraySegment<byte> Bytes { get; }

        public ArraySegment<byte>[] Substreams(int rbspOffset, int[] encodedLengths)
        {
            if ((rbspOffset < 0) || (rbspOffset >= Bytes.Count))
            {
                throw new InvalidDataException("HEVC entropy data offset exceeds the NAL payload.");
            }
            int encodedPosition = 0;
            int rbspPosition = 0;
            int zeros = 0;
            while (rbspPosition < rbspOffset)
            {
                Advance();
            }

            ArraySegment<byte>[] streams = new ArraySegment<byte>[encodedLengths.Length + 1];
            for (int i = 0; i < encodedLengths.Length; i++)
            {
                int length = encodedLengths[i];
                if ((length <= 0) || (length >= (encoded.Length - encodedPosition)))
                {
                    throw new InvalidDataException("HEVC entry point exceeds the NAL payload.");
                }
                int end = encodedPosition + length;
                int start = rbspPosition;
                while (encodedPosition < end)
                {
                    Advance();
                }
                streams[i] = Bytes.Slice(start, rbspPosition - start);
            }
            streams[streams.Length - 1] = Bytes.Slice(rbspPosition);
            return streams;

            void Advance()
            {
                byte value = encoded.Span[encodedPosition++];
                if ((encodedPosition > 2) && (zeros >= 2) && (value == 3))
                {
                    zeros = 0;
                    return;
                }
                rbspPosition++;
                zeros = value == 0 ? zeros + 1 : 0;
            }
        }

        public static IReadOnlyList<NalPayload> ReadLengthPrefixed(ReadOnlyMemory<byte> data, int lengthSize)
        {
            if ((lengthSize < 1) || (lengthSize > 4))
            {
                throw new InvalidDataException("Invalid NAL length field size.");
            }
            ByteReader reader = new ByteReader(data);
            List<NalPayload> units = new List<NalPayload>();
            while (reader.Remaining > 0)
            {
                ulong length = reader.UInt(lengthSize);
                if (length > (ulong)reader.Remaining)
                {
                    throw new InvalidDataException("NAL length exceeds image item.");
                }
                units.Add(new NalPayload(reader.Bytes(checked((int)length))));
            }

            return units;
        }
    }
}

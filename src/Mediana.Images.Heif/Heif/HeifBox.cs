// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Images.Heif.Binary;
using System;
using System.IO;

namespace Mediana.Images.Heif.Container
{
    internal readonly struct HeifBox(uint type, int offset, ReadOnlyMemory<byte> payload)
    {
        public uint Type { get; } = type;
        public int Offset { get; } = offset;
        public ReadOnlyMemory<byte> Payload { get; } = payload;

        public static HeifBox Read(ByteReader reader, int baseOffset)
        {
            Mediana.Formats.IsoBmff.IsoBmffBoxReader boxes = new(reader.Unread, checked(baseOffset + reader.Position));
            if (!boxes.TryRead(out Mediana.Formats.IsoBmff.IsoBmffBox box)) { throw new InvalidDataException("Missing HEIF box."); }
            reader.Skip(boxes.Position);
            return new(box.Type, box.PayloadOffset, box.Payload);
        }

        public static uint Code(string text) => Mediana.Formats.IsoBmff.IsoBmffBox.Code(text);
    }
}

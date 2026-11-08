// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.CompilerServices;

namespace Mediana.Codecs.Av1.Tests
{
    internal static class Av1TilePrecisionTestInput
    {
        internal static void Verify(string root, string fixture)
        {
            using GZipStream source = new(File.OpenRead(Path.Combine(root, fixture + ".tile.bin.gz")), CompressionMode.Decompress);
            using BinaryReader reader = new(source);
            Require(reader.ReadBytes(8).AsSpan().SequenceEqual("AV1TILE1"u8));
            int width = reader.ReadUInt16();
            int height = reader.ReadUInt16();
            int encodedLength = reader.ReadInt32();
            int expectedLength = reader.ReadInt32();
            Require(width > 0 && height > 0 && encodedLength > 0 && expectedLength > 0);
            byte[] encoded = reader.ReadBytes(encodedLength);
            byte[] expected = reader.ReadBytes(expectedLength);
            Require(encoded.Length == encodedLength && expected.Length == expectedLength && source.ReadByte() == -1);
            Av1ObuReader obus = new(encoded);
            Av1SequenceHeader? sequence = null;
            ReadOnlySpan<byte> frame = default;
            while (obus.TryRead(out int type, out ReadOnlySpan<byte> payload))
            {
                if (type == 1) { sequence = Av1SequenceHeader.Parse(payload); }
                if (type == 6) { frame = payload; }
            }
            Av1SequenceHeader geometry = sequence ?? throw new InvalidDataException("The sequence OBU is missing.");
            Require(geometry.BitDepth == 10 && geometry.Width == width && geometry.Height == height);
            Require(geometry.SampleCount * 2 == geometry.SampleByteCount);
            Require(Unsafe.SizeOf<Av1BlockContext<ushort>>() > Unsafe.SizeOf<Av1BlockContext<byte>>());
            using Av1ReferencePictures references = new();
            Av1FrameHeader header = Av1FrameHeader.Parse(frame, geometry, references);
            geometry = header.Geometry;
            Require(!header.LoopFilter.Enabled);
            Require(!header.Cdef.Enabled);
            Require(!header.Restoration.Enabled);
            Require(!header.SuperResolution.Active);
            Require(header.FrameType is Av1FrameType.Key or Av1FrameType.IntraOnly);
            ushort[] output = new ushort[geometry.SampleCount];
            ushort[] probabilities = new ushort[Av1DefaultCdfs.Count];
            byte[] decoded = new byte[geometry.SampleCount / 16];
            Av1BlockContext<ushort>[] contexts = new Av1BlockContext<ushort>[geometry.ContextCount];
            Av1MotionBlock[] motions = header.Intrabc ? new Av1MotionBlock[geometry.MiColumns * geometry.MiRows] : [];
            Av1TilePayloadReader tiles = new(frame[header.TileOffset..], header.Tiles, true);
            int count = 0;
            while (tiles.TryRead(out int index, out ReadOnlySpan<byte> tileBytes, default))
            {
                Av1TileDecoder<ushort> tile = new(tileBytes, geometry, header, output, probabilities, decoded, contexts, default,
                    references: references, motionBlocks: motions, tileIndex: index);
                tile.Decode();
                count++;
            }
            Require(header.Tiles.Count == count);
            int expectedPosition = 0;
            for (int plane = 0; plane < (geometry.Monochrome ? 1 : 3); plane++)
            {
                int sub = geometry.PlaneShift(plane);
                int planeWidth = (geometry.Width + sub) >> sub;
                int planeHeight = (geometry.Height + sub) >> sub;
                int stride = geometry.PlaneStride(plane);
                int offset = geometry.PlaneOffset(plane);
                for (int y = 0; y < planeHeight; y++)
                {
                    for (int x = 0; x < planeWidth; x++)
                    {
                        ushort sample = BinaryPrimitives.ReadUInt16LittleEndian(expected.AsSpan(expectedPosition));
                        expectedPosition += 2;
                        ushort actual = output[offset + y * stride + x];
                        if (sample != actual)
                        {
                            throw new InvalidDataException($"{fixture} plane {plane} at {x},{y}: expected {sample}, actual {actual}.");
                        }
                    }
                }
            }
            Require(expected.Length == expectedPosition);
        }

        private static void Require(bool condition)
        {
            if (!condition) { throw new InvalidDataException("The typed tile metadata, sample storage or output extent is invalid."); }
        }
    }
}

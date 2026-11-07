// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using Mediana.Codecs.Vorbis;
using Mediana.Core.Memory;
using Mediana.Formats.Audio;

namespace Mediana.Tests
{
    internal static class VorbisCodebookTestInput
    {
        public static int Verify(string root)
        {
            using FileStream file = File.OpenRead(Path.Combine(root, "codebooks.bin.gz"));
            using GZipStream gzip = new(file, CompressionMode.Decompress);
            using BinaryReader input = new(gzip);
            if (!input.ReadBytes(8).AsSpan().SequenceEqual("VORBOOK1"u8)) { throw new InvalidDataException("The Vorbis codebook reference header is invalid."); }
            int count = input.ReadInt32(), operations = 0;
            Span<byte> identification = stackalloc byte[30];
            identification.Clear();
            "\u0001vorbis"u8.CopyTo(identification);
            identification[11] = 1;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(identification[12..], 44100);
            identification[28] = 0xb8; identification[29] = 1;
            VorbisIdentification info = VorbisIdentification.Read(identification);
            MediaBufferAllocator allocator = new(16 * 1024 * 1024);
            float[] vector = new float[16];
            for (int test = 0; test < count; test++)
            {
                byte[] setup = input.ReadBytes(input.ReadInt32());
                int dimensions = input.ReadInt32(), entries = input.ReadInt32(), lookup = input.ReadInt32();
                byte[] data = input.ReadBytes(input.ReadInt32());
                int symbols = input.ReadInt32();
                int[] expected = new int[symbols];
                for (int symbol = 0; symbol < symbols; symbol++) { expected[symbol] = input.ReadInt32(); }
                float[] values = lookup == 0 ? [] : new float[entries * dimensions];
                for (int value = 0; value < values.Length; value++) { values[value] = input.ReadSingle(); }
                using (VorbisSetupCapture capture = new(allocator))
                {
                    VorbisSetupInfo.ReadForDecoding(info, setup, capture);
                    if (capture.Books.Count != 1) { throw new InvalidDataException("The reference must declare one Vorbis codebook."); }
                    VorbisCodebook book = capture.Books[0];
                    if (book.Dimensions != dimensions || book.Entries != entries || book.HasVectors != (lookup != 0))
                    {
                        throw new InvalidDataException("The captured Vorbis codebook declaration differs.");
                    }
                    VorbisBitReader reader = new(data);
                    for (int symbol = 0; symbol < symbols; symbol++)
                    {
                        if (!book.TryDecode(ref reader, out int entry) || entry != expected[symbol])
                        {
                            throw new InvalidDataException($"Vorbis codebook {test}, symbol {symbol}: {entry}/{expected[symbol]}.");
                        }
                        if (lookup != 0)
                        {
                            book.ReadVector(entry, vector);
                            for (int dimension = 0; dimension < dimensions; dimension++)
                            {
                                float reference = values[entry * dimensions + dimension];
                                if (Math.Abs(vector[dimension] - reference) > 2e-7 * Math.Max(1, Math.Abs(reference)))
                                {
                                    throw new InvalidDataException($"Vorbis codebook {test}, entry {entry}, dimension {dimension}: {vector[dimension]:R}/{reference:R}.");
                                }
                            }
                        }
                        operations++;
                    }
                }
                if (allocator.AllocatedBytes != 0) { throw new InvalidDataException("Vorbis codebook storage was not released."); }
            }
            if (input.BaseStream.ReadByte() != -1) { throw new InvalidDataException("The Vorbis codebook reference has trailing data."); }
            return operations;
        }
    }
}

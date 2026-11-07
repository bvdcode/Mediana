// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Audio;

namespace Mediana.Tests
{
    internal static class OpusConfigurationTestInput
    {
        public static int Verify(string root)
        {
            using BinaryReader reader = new(File.OpenRead(Path.Combine(root, "packet-configurations.bin")));
            if (!reader.ReadBytes(8).AsSpan().SequenceEqual("OPUSTOC1"u8))
            {
                throw new InvalidDataException("The Opus packet-configuration reference header is invalid.");
            }
            int cases = reader.ReadInt32();
            for (int index = 0; index < cases; index++)
            {
                OpusPacketConfiguration configuration = OpusPacketConfiguration.FromToc(reader.ReadByte());
                if (configuration.CodedChannelCount != reader.ReadInt32() || configuration.SamplesPerFrame != reader.ReadInt32() ||
                    (int)configuration.Bandwidth != reader.ReadInt32())
                {
                    throw new InvalidDataException($"Opus packet configuration {index} differs from the independent decoder.");
                }
            }
            if (reader.BaseStream.Position != reader.BaseStream.Length)
            {
                throw new InvalidDataException("The Opus packet-configuration reference has trailing bytes.");
            }
            return cases;
        }
    }
}

// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core;
using Mediana.Core.Metadata;
using Mediana.Core.Timing;

namespace Mediana.Tests
{
    internal static class H264MetadataTestInput
    {
        public static byte[] Sequence(int id = 0, int widthMbs = 4, int heightMapUnits = 3, bool frameOnly = true,
            int profile = 66, int chroma = 1, int depth = 8, string? vui = null)
        {
            string bits = Convert.ToString(profile, 2).PadLeft(8, '0') + " 00000000 00001010 " + H264TestBits.Ue(id);
            if (profile is 100 or 110 or 122 or 244)
            {
                bits += H264TestBits.Ue(chroma);
                if (chroma == 3)
                {
                    bits += "0";
                }
                bits += H264TestBits.Ue(depth - 8) + H264TestBits.Ue(depth - 8) + "00";
            }
            bits += H264TestBits.Ue(0) + H264TestBits.Ue(0) + H264TestBits.Ue(0) + H264TestBits.Ue(1) + "0";
            bits += H264TestBits.Ue(widthMbs - 1) + H264TestBits.Ue(heightMapUnits - 1);
            bits += frameOnly ? "1" : "00";
            bits += "10";
            bits += vui is null ? "0" : "1" + vui;
            return [0x67, .. H264TestBits.Rbsp(bits)];
        }

        public static byte[] Picture(int id = 0, int sequenceId = 0, bool groups = false)
        {
            string bits = H264TestBits.Ue(id) + H264TestBits.Ue(sequenceId) + "00";
            bits += groups ? H264TestBits.Ue(1) + H264TestBits.Ue(0) + H264TestBits.Ue(0) + H264TestBits.Ue(0) : H264TestBits.Ue(0);
            bits += H264TestBits.Ue(0) + H264TestBits.Ue(0) + "0 00 1 1 1 1 0 0";
            return [0x68, .. H264TestBits.Rbsp(bits)];
        }

        public static byte[] Configuration(byte[][] sequences, byte[][] pictures)
        {
            List<byte> bytes = [1, sequences[0][1], sequences[0][2], sequences[0][3], 0xff, (byte)(0xe0 | sequences.Length)];
            foreach (byte[] nal in sequences)
            {
                bytes.Add((byte)(nal.Length >> 8));
                bytes.Add((byte)nal.Length);
                bytes.AddRange(nal);
            }
            bytes.Add((byte)pictures.Length);
            foreach (byte[] nal in pictures)
            {
                bytes.Add((byte)(nal.Length >> 8));
                bytes.Add((byte)nal.Length);
                bytes.AddRange(nal);
            }
            return bytes.ToArray();
        }

        public static EncodedStreamInfo Stream(byte[] configuration) => new(7, MediaKind.Video, "h264", new TimeBase(1, 90000), configuration);

        public static void VerifyMultipleConfigurations()
        {
            byte[] bytes = Configuration([Sequence(27, widthMbs: 6), Sequence(3)], [Picture(sequenceId: 27)]);
            StreamMetadata? value = new H264StreamMetadataReader().Read(Stream(bytes));
            if (value is not VideoStreamMetadata metadata || metadata.Configurations.Count != 2
                || metadata.Configurations[0] is not H264SequenceMetadata first || metadata.Configurations[1] is not H264SequenceMetadata second
                || first.SequenceId != 3 || first.Width != 64 || second.SequenceId != 27 || second.Width != 96)
            {
                throw new InvalidDataException("The opening SPS alternatives were not retained independently.");
            }
        }
    }
}

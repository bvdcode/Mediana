// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    internal static class H264CabacPredictionTestInput
    {
        private static readonly string?[] TypeBins =
        [
            "000", "011", "010", "001", null, "10",
            "1100000", "1100001", "1100010", "1100011",
            "11001000", "11001001", "11001010", "11001011",
            "11001100", "11001101", "11001110", "11001111",
            "1101000", "1101001", "1101010", "1101011",
            "11011000", "11011001", "11011010", "11011011",
            "11011100", "11011101", "11011110", "11011111", "111"
        ];

        public static async Task VerifyAsync(string root, string mediaRoot, int initialization)
        {
            byte[] states = await File.ReadAllBytesAsync(Path.Combine(root, "cabac-predictive-contexts.bin"));
            H264SliceHeader sourceHeader = await ReadHeaderAsync(mediaRoot);
            foreach (int qp in new[] { 0, 25, 51 })
            {
                H264SliceHeader header = sourceHeader with { Qp = qp, CabacInitIdc = initialization };
                VerifyTypes(states, header);
                VerifySkipAndSubtypes(states, header);
                VerifyReferences(states, header);
                foreach (bool vertical in new[] { false, true })
                {
                    foreach (int sum in new[] { 0, 2, 3, 32, 33, 65536 })
                    {
                        VerifyMotion(states, header, vertical, sum);
                    }
                }
            }
        }

        public static async Task<H264SliceHeader> ReadHeaderAsync(string root)
        {
            (H264Configuration configuration, byte[] nal) = await H264TestInput.ReadSliceAsync(Path.Combine(root, "avc-aac.mp4"), 1);
            H264BitReader bits = H264Nal.Payload(nal, H264Nal.Type(nal));
            return H264SliceHeaderReader.Read(ref bits, configuration, nal[0]);
        }

        public static H264CabacIntervalWriter CreateWriter(byte[] states, H264SliceHeader header)
        {
            H264CabacIntervalWriter writer = new(header.Qp);
            int[] indices = Enumerable.Range(11, 13).Concat(Enumerable.Range(40, 236)).Concat(Enumerable.Range(399, 3)).ToArray();
            int offset = (header.CabacInitIdc * 52 + header.Qp) * indices.Length;
            for (int index = 0; index < indices.Length; index++)
            {
                writer.Initialize(indices[index], states[offset + index]);
            }
            return writer;
        }

        private static void VerifyTypes(byte[] states, H264SliceHeader header)
        {
            for (int type = 0; type < TypeBins.Length; type++)
            {
                string? bins = TypeBins[type];
                if (bins is null)
                {
                    continue;
                }
                H264CabacIntervalWriter writer = CreateWriter(states, header);
                for (int index = 0; index < bins.Length; index++)
                {
                    bool value = bins[index] == '1';
                    if (bins[0] == '1' && index == 2)
                    {
                        writer.Termination(value);
                        continue;
                    }
                    int context = index switch
                    {
                        0 => 14,
                        1 => bins[0] == '1' ? 17 : 15,
                        2 => bins[1] == '1' ? 17 : 16,
                        3 => 18,
                        4 => 19,
                        5 => bins[4] == '1' ? 19 : 20,
                        6 or 7 => 20,
                        _ => throw new InvalidDataException("The normative P macroblock code has an invalid length.")
                    };
                    writer.Decision(value, context, 0, 0);
                }
                if (type != 30)
                {
                    writer.Termination(true);
                }
                H264BitReader bits = new(writer.ToEbsp());
                H264CabacReader reader = new(ref bits, header);
                if (H264CabacMacroblockSyntax.ReadPredictiveType(ref bits, reader) != type ||
                    (type != 30 && !reader.Termination(ref bits)))
                {
                    throw new InvalidDataException($"The H.264 CABAC P macroblock type {type} differs from its normative bin string.");
                }
            }
        }

        private static void VerifySkipAndSubtypes(byte[] states, H264SliceHeader header)
        {
            H264CabacIntervalWriter writer = CreateWriter(states, header);
            for (int context = 0; context < 3; context++)
            {
                writer.Decision(false, 11 + context, 0, 0);
                writer.Decision(true, 11 + context, 0, 0);
            }
            string[] codes = ["1", "00", "011", "010"];
            foreach (string code in codes)
            {
                for (int index = 0; index < code.Length; index++)
                {
                    writer.Decision(code[index] == '1', 21 + index, 0, 0);
                }
            }
            writer.Termination(true);
            H264BitReader bits = new(writer.ToEbsp());
            H264CabacReader reader = new(ref bits, header);
            for (int context = 0; context < 3; context++)
            {
                if (H264CabacMacroblockSyntax.ReadSkip(ref bits, reader, context) ||
                    !H264CabacMacroblockSyntax.ReadSkip(ref bits, reader, context))
                {
                    throw new InvalidDataException("The H.264 CABAC skip flag differs from its normative bin string.");
                }
            }
            for (int type = 0; type < codes.Length; type++)
            {
                if (H264CabacMacroblockSyntax.ReadSubType(ref bits, reader) != type)
                {
                    throw new InvalidDataException("The H.264 CABAC P sub-macroblock type differs from its normative bin string.");
                }
            }
            if (!reader.Termination(ref bits))
            {
                throw new InvalidDataException("The H.264 CABAC skip/subtype interval did not terminate.");
            }
        }

        private static void VerifyReferences(byte[] states, H264SliceHeader header)
        {
            H264CabacIntervalWriter writer = CreateWriter(states, header);
            for (int condition = 0; condition < 4; condition++)
            {
                for (int reference = 0; reference <= 31; reference++)
                {
                    for (int index = 0; index <= reference; index++)
                    {
                        int context = index == 0 ? 54 + condition : index == 1 ? 58 : 59;
                        writer.Decision(index < reference, context, 0, 0);
                    }
                }
            }
            writer.Termination(true);
            H264BitReader bits = new(writer.ToEbsp());
            H264CabacReader reader = new(ref bits, header);
            for (int condition = 0; condition < 4; condition++)
            {
                for (int reference = 0; reference <= 31; reference++)
                {
                    if (H264CabacMacroblockSyntax.ReadReference(ref bits, reader, condition, 31) != reference)
                    {
                        throw new InvalidDataException("The H.264 CABAC reference index differs from its unary bin string.");
                    }
                }
            }
            if (!reader.Termination(ref bits))
            {
                throw new InvalidDataException("The H.264 CABAC reference interval did not terminate.");
            }
        }

        private static void VerifyMotion(byte[] states, H264SliceHeader header, bool vertical, int sum)
        {
            int[] values = [0, 1, -1, 2, -2, 8, -8, 9, -9, 16, -16, 17, -17, 32767, -32768];
            H264CabacIntervalWriter writer = CreateWriter(states, header);
            foreach (int value in values)
            {
                WriteMotion(writer, value, vertical, sum);
            }
            writer.Termination(true);
            H264BitReader bits = new(writer.ToEbsp());
            H264CabacReader reader = new(ref bits, header);
            foreach (int expected in values)
            {
                if (H264CabacMacroblockSyntax.ReadMotionDifference(ref bits, reader, vertical, sum) != expected)
                {
                    throw new InvalidDataException("The H.264 CABAC motion difference does not match its UEG3 bin string.");
                }
            }
            if (!reader.Termination(ref bits))
            {
                throw new InvalidDataException("The H.264 CABAC motion interval did not terminate.");
            }
        }

        public static void WriteMotion(H264CabacIntervalWriter writer, int value, bool vertical, int sum)
        {
            int magnitude = Math.Abs(value);
            int offset = vertical ? 47 : 40;
            int[] contexts = [offset + (sum <= 2 ? 0 : sum <= 32 ? 1 : 2), offset + 3, offset + 4, offset + 5, offset + 6];
            for (int index = 0; index < Math.Min(9, magnitude + 1); index++)
            {
                writer.Decision(index < magnitude, contexts[Math.Min(4, index)], 0, 0);
            }
            if (magnitude >= 9)
            {
                int remainder = magnitude - 9;
                int order = 3;
                while (remainder >= 1 << order)
                {
                    writer.Bits(1, 1);
                    remainder -= 1 << order++;
                }
                writer.Bits(0, 1);
                writer.Bits((uint)remainder, order);
            }
            if (value != 0)
            {
                writer.Bits(value < 0 ? 1u : 0u, 1);
            }
        }
    }
}

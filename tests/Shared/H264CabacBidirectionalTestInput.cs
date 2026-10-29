// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class H264CabacBidirectionalTestInput
    {
        private static readonly string[] InterTypes =
        [
            "0", "100", "101", "110000", "110001", "110010", "110011", "110100", "110101", "110110", "110111", "111110",
            "1110000", "1110001", "1110010", "1110011", "1110100", "1110101", "1110110", "1110111", "1111000", "1111001", "111111"
        ];

        private static readonly string[] IntraTypes =
        [
            "0", "100000", "100001", "100010", "100011", "1001000", "1001001", "1001010", "1001011",
            "1001100", "1001101", "1001110", "1001111", "101000", "101001", "101010", "101011",
            "1011000", "1011001", "1011010", "1011011", "1011100", "1011101", "1011110", "1011111", "11"
        ];

        public static async Task VerifyAsync(string root, string mediaRoot)
        {
            byte[] states = await File.ReadAllBytesAsync(Path.Combine(root, "cabac-bidirectional-contexts.bin"));
            byte[] shared = await File.ReadAllBytesAsync(Path.Combine(root, "cabac-predictive-contexts.bin"));
            if (states.Length != 3 * 52 * 16)
            {
                throw new InvalidDataException("The B context reference has an unexpected size.");
            }
            H264SliceHeader source = await H264CabacPredictionTestInput.ReadHeaderAsync(mediaRoot);
            for (int initialization = 0; initialization < 3; initialization++)
            {
                for (int qp = 0; qp < 52; qp++)
                {
                    H264CabacContext[] contexts = H264CabacBidirectionalContexts.Create(qp, initialization);
                    for (int index = 0; index < 16; index++)
                    {
                        if (contexts[index].State * 2 + (contexts[index].MostProbableSymbol ? 1 : 0) != states[(initialization * 52 + qp) * 16 + index])
                        {
                            throw new InvalidDataException("A B context initialization differs from Table 9-14.");
                        }
                    }
                }
                foreach (int qp in new[] { 0, 25, 51 })
                {
                    H264SliceHeader header = source with { Kind = H264SliceKind.B, Qp = qp, CabacInitIdc = initialization };
                    VerifyTypes(states, shared, header);
                    VerifySkipAndSubtypes(states, shared, header);
                    VerifyPredictionState(states, shared, header);
                }
            }
        }

        public static H264CabacIntervalWriter Writer(byte[] states, byte[] shared, H264SliceHeader header)
        {
            H264CabacIntervalWriter writer = H264CabacPredictionTestInput.CreateWriter(shared, header);
            int offset = (header.CabacInitIdc * 52 + header.Qp) * 16;
            for (int index = 0; index < 16; index++)
            {
                writer.Initialize(24 + index, states[offset + index]);
            }
            return writer;
        }

        private static void VerifyPredictionState(byte[] states, byte[] shared, H264SliceHeader source)
        {
            H264SequenceParameterSet sequence = new() { WidthInMbs = 3, HeightInMapUnits = 1, FrameMbsOnly = true };
            H264SliceHeader header = source with { Sequence = sequence };
            H264CabacIntervalWriter writer = Writer(states, shared, header);
            foreach ((int dx, int dy, int sx, int sy) in new[] { (40, -3, 0, 0), (-4, 50, 0, 0), (2, 1, 40, 3), (1, -2, 4, 50) })
            {
                H264CabacPredictionTestInput.WriteMotion(writer, dx, false, sx);
                H264CabacPredictionTestInput.WriteMotion(writer, dy, true, sy);
            }
            writer.Decision(true, 54, 0, 0);
            writer.Decision(false, 58, 0, 0);
            writer.Decision(false, 54, 0, 0);
            writer.Decision(false, 55, 0, 0);
            writer.Decision(true, 54, 0, 0);
            writer.Decision(false, 58, 0, 0);
            writer.Decision(false, 54, 0, 0);
            writer.Termination(true);
            MediaBufferAllocator allocator = new(8192);
            using (H264Picture target = new(sequence, allocator, H264SliceKind.B))
            {
                H264BitReader bits = new(writer.ToEbsp());
                H264CabacReader reader = new(ref bits, header);
                using H264CabacPredictionReader prediction = new(reader, target, allocator);
                if (prediction.ReadMotionDifference(ref bits, H264ReferenceList.L0, 0, 0, 16, 16) != (40, -3)
                    || prediction.ReadMotionDifference(ref bits, H264ReferenceList.L1, 0, 0, 16, 16) != (-4, 50)
                    || prediction.ReadMotionDifference(ref bits, H264ReferenceList.L0, 16, 0, 16, 16) != (2, 1)
                    || prediction.ReadMotionDifference(ref bits, H264ReferenceList.L1, 16, 0, 16, 16) != (1, -2)
                    || prediction.ReadReference(ref bits, H264ReferenceList.L0, 0, 0, 16, 16, 1) != 1
                    || prediction.ReadReference(ref bits, H264ReferenceList.L1, 0, 0, 16, 16, 1) != 0
                    || prediction.ReadReference(ref bits, H264ReferenceList.L0, 16, 0, 16, 16, 1) != 0
                    || prediction.ReadReference(ref bits, H264ReferenceList.L1, 16, 0, 16, 16, 1) != 1)
                {
                    throw new InvalidDataException("The CABAC B motion or reference contexts share state between lists.");
                }
                target.SetMotion(H264ReferenceList.L0, 16, 0, 16, 16, 1, 100, 100);
                if (prediction.ReadReference(ref bits, H264ReferenceList.L0, 32, 0, 16, 16, 1) != 0 || !reader.Termination(ref bits))
                {
                    throw new InvalidDataException("Inferred direct references changed the CABAC syntax context.");
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("CABAC B syntax verification retained picture storage.");
            }
        }

        private static void VerifyTypes(byte[] states, byte[] shared, H264SliceHeader header)
        {
            for (int context = 0; context < 3; context++)
            {
                for (int type = 0; type <= 48; type++)
                {
                    H264CabacIntervalWriter writer = Writer(states, shared, header);
                    string prefix = type < 23 ? InterTypes[type] : "111101";
                    for (int index = 0; index < prefix.Length; index++)
                    {
                        int slot = index switch { 0 => 27 + context, 1 => 30, 2 => prefix[1] == '1' ? 31 : 32, _ => 32 };
                        writer.Decision(prefix[index] == '1', slot, 0, 0);
                    }
                    if (type >= 23)
                    {
                        string suffix = IntraTypes[type - 23];
                        for (int index = 0; index < suffix.Length; index++)
                        {
                            if (index == 1)
                            {
                                writer.Termination(suffix[index] == '1');
                            }
                            else
                            {
                                int slot = index switch { 0 => 32, 2 => 33, 3 => 34, 4 => suffix[3] == '1' ? 34 : 35, _ => 35 };
                                writer.Decision(suffix[index] == '1', slot, 0, 0);
                            }
                        }
                    }
                    if (type != 48)
                    {
                        writer.Termination(true);
                    }
                    H264BitReader bits = new(writer.ToEbsp());
                    H264CabacReader reader = new(ref bits, header);
                    if (H264CabacMacroblockSyntax.ReadBidirectionalType(ref bits, reader, context) != type
                        || type != 48 && !reader.Termination(ref bits))
                    {
                        throw new InvalidDataException($"The B CABAC macroblock type {type} differs from its normative bin string.");
                    }
                }
            }
        }

        private static void VerifySkipAndSubtypes(byte[] states, byte[] shared, H264SliceHeader header)
        {
            H264CabacIntervalWriter writer = Writer(states, shared, header);
            for (int context = 0; context < 3; context++)
            {
                writer.Decision(false, 24 + context, 0, 0);
                writer.Decision(true, 24 + context, 0, 0);
            }
            string[] subtypes = ["0", "100", "101", "11000", "11001", "11010", "11011", "111000", "111001", "111010", "111011", "11110", "11111"];
            foreach (string code in subtypes)
            {
                for (int index = 0; index < code.Length; index++)
                {
                    int slot = index switch { 0 => 36, 1 => 37, 2 => code[1] == '1' ? 38 : 39, _ => 39 };
                    writer.Decision(code[index] == '1', slot, 0, 0);
                }
            }
            writer.Termination(true);
            H264BitReader bits = new(writer.ToEbsp());
            H264CabacReader reader = new(ref bits, header);
            for (int context = 0; context < 3; context++)
            {
                if (H264CabacMacroblockSyntax.ReadBidirectionalSkip(ref bits, reader, context)
                    || !H264CabacMacroblockSyntax.ReadBidirectionalSkip(ref bits, reader, context))
                {
                    throw new InvalidDataException("B CABAC skip bins differ from their exact interval.");
                }
            }
            for (int subtype = 0; subtype < subtypes.Length; subtype++)
            {
                if (H264CabacMacroblockSyntax.ReadBidirectionalSubType(ref bits, reader) != subtype)
                {
                    throw new InvalidDataException("A B CABAC sub-macroblock type differs from its normative bin string.");
                }
            }
            if (!reader.Termination(ref bits))
            {
                throw new InvalidDataException("The B syntax interval did not terminate.");
            }
        }
    }
}

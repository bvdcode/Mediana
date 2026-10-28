// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    internal static class H264CabacTestInput
    {
        public static async Task VerifyIntraContextStatesAsync(string root)
        {
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, "cabac-intra-contexts.bin"));
            int[] indices = Enumerable.Range(3, 8).Concat(Enumerable.Range(60, 216)).Concat(Enumerable.Range(399, 3)).ToArray();
            if (expected.Length != indices.Length * 52)
            {
                throw new InvalidDataException("The H.264 intra context reference has an unexpected length.");
            }
            int offset = 0;
            for (int qp = 0; qp <= 51; qp++)
            {
                H264CabacContext[] contexts = H264CabacIntraContexts.Create(qp);
                foreach (int index in indices)
                {
                    H264CabacContext context = contexts[H264CabacIntraContexts.GetIndex(index)];
                    int packed = context.State * 2 + (context.MostProbableSymbol ? 1 : 0);
                    if (packed != expected[offset++])
                    {
                        throw new InvalidDataException($"H.264 intra context mismatch at QP {qp}, context {index}.");
                    }
                }
            }
        }

        public static async Task VerifyRealIntraPrefixAsync(string root)
        {
            (H264Configuration configuration, byte[] nal) = await H264TestInput.ReadSliceAsync(Path.Combine(root, "avc-aac.mp4"), 0);
            H264BitReader bits = H264Nal.Payload(nal, H264Nal.Type(nal));
            H264SliceHeader header = H264SliceHeaderReader.Read(ref bits, configuration, nal[0]);
            if (!header.Picture.Cabac || header.Kind != H264SliceKind.I || header.Qp != 23 || header.FrameNumber != 0)
            {
                throw new InvalidDataException("The H.264 intra reference has unexpected slice syntax.");
            }
            H264CabacIntraReader reader = new(ref bits, header);
            if (reader.Read(ref bits, 3) || reader.Read(ref bits, 399))
            {
                throw new InvalidDataException("The H.264 intra reference does not start with the expected 4x4 intra macroblock.");
            }
        }

        public static void VerifyMixedBins()
        {
            sbyte[] slopes = [20, 2, 3, -28, -23, -6, -1, 7];
            sbyte[] intercepts = [-15, 54, 74, 127, 104, 53, 54, 51];
            int[] quantizers = [-6, 0, 17, 26, 51, 63];
            int[] widths = [0, 1, 7, 16, 32];
            bool[] rangeColumns = new bool[4];
            foreach (int qp in quantizers)
            {
                for (int sequence = 0; sequence < 4; sequence++)
                {
                    Random random = new(71653 + sequence);
                    bool[] values = new bool[128];
                    int[] slots = new int[values.Length];
                    uint[] bypass = new uint[values.Length];
                    H264CabacIntervalWriter writer = new(qp);
                    for (int index = 0; index < values.Length; index++)
                    {
                        slots[index] = random.Next(slopes.Length);
                        values[index] = random.Next(2) != 0;
                        int count = widths[index % widths.Length];
                        bypass[index] = (uint)random.NextInt64(1L << count);
                        int slot = slots[index];
                        writer.Decision(values[index], slot, slopes[slot], intercepts[slot]);
                        writer.Bits(bypass[index], count);
                        writer.Termination(false);
                    }
                    writer.Termination(true);

                    H264BitReader reader = new(writer.ToEbsp());
                    H264CabacDecoder decoder = new(ref reader);
                    H264CabacContext[] contexts = new H264CabacContext[slopes.Length];
                    for (int slot = 0; slot < contexts.Length; slot++)
                    {
                        contexts[slot] = H264CabacContext.Initialize(slopes[slot], intercepts[slot], qp);
                    }
                    for (int index = 0; index < values.Length; index++)
                    {
                        rangeColumns[(decoder.Range >> 6) & 3] = true;
                        if (decoder.ReadBin(ref reader, ref contexts[slots[index]]) != values[index] ||
                            decoder.ReadBypassBits(ref reader, widths[index % widths.Length]) != bypass[index] ||
                            decoder.ReadTermination(ref reader))
                        {
                            throw new InvalidDataException($"CABAC interval mismatch at QP {qp}, sequence {sequence}, decision {index}.");
                        }
                        if (decoder.Range < 256 || decoder.Range > 510 || decoder.Offset < 0 || decoder.Offset >= decoder.Range)
                        {
                            throw new InvalidDataException("CABAC violated its normalized interval bounds.");
                        }
                    }
                    if (!decoder.ReadTermination(ref reader))
                    {
                        throw new InvalidDataException("CABAC did not decode the final termination bin.");
                    }
                }
            }
            if (rangeColumns.Any(covered => !covered))
            {
                throw new InvalidDataException("CABAC interval checks did not cover every probability range column.");
            }
        }
    }
}

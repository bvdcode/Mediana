// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class H264CabacTestInput
    {
        public static async Task VerifyIntraPicturesAsync(string root)
        {
            byte[] first = await File.ReadAllBytesAsync(Path.Combine(root, "avc-aac.frame0.yuv"));
            VerifyIntraPicture(await H264TestInput.ReadSliceAsync(Path.Combine(root, "avc-aac.mp4"), 0), first);
            byte[] sequence = await File.ReadAllBytesAsync(Path.Combine(root, "avc-cabac-intra.sequence.yuv"));
            const int pictureBytes = 94 * 62 * 3 / 2;
            if (sequence.Length != pictureBytes * 6)
            {
                throw new InvalidDataException("The H.264 CABAC reference sequence has an unexpected length.");
            }
            for (int index = 0; index < 6; index++)
            {
                VerifyIntraPicture(await H264TestInput.ReadSliceAsync(Path.Combine(root, "avc-cabac-intra.mp4"), index),
                    sequence.AsSpan(index * pictureBytes, pictureBytes));
            }
        }

        private static void VerifyIntraPicture((H264Configuration Configuration, byte[] Nal) input, ReadOnlySpan<byte> expected)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            H264BitReader bits = H264Nal.Payload(input.Nal, H264Nal.Type(input.Nal));
            H264SliceHeader header = H264SliceHeaderReader.Read(ref bits, input.Configuration, input.Nal[0]);
            using (H264Picture picture = H264CabacPictureDecoder.Decode(ref bits, header, allocator))
            {
                H264TestInput.VerifyPicture(picture, expected);
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("The H.264 CABAC picture or worker storage was not released.");
            }
        }

        public static Task VerifyIntraContextStatesAsync(string root) =>
            VerifyContextStatesAsync(root, "cabac-intra-contexts.bin", 1,
                Enumerable.Range(3, 8).Concat(Enumerable.Range(60, 216)).Concat(Enumerable.Range(399, 3)).ToArray(),
                (qp, _) => H264CabacIntraContexts.Create(qp), H264CabacIntraContexts.GetIndex);

        public static Task VerifyPredictiveContextStatesAsync(string root) =>
            VerifyContextStatesAsync(root, "cabac-predictive-contexts.bin", 3,
                Enumerable.Range(11, 13).Concat(Enumerable.Range(40, 236)).Concat(Enumerable.Range(399, 3)).ToArray(),
                H264CabacPredictiveContexts.Create, H264CabacPredictiveContexts.GetIndex);

        private static async Task VerifyContextStatesAsync(string root, string fileName, int initializations, int[] indices,
            Func<int, int, H264CabacContext[]> create, Func<int, int> getIndex)
        {
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, fileName));
            if (expected.Length != indices.Length * 52 * initializations)
            {
                throw new InvalidDataException("The H.264 context reference has an unexpected length.");
            }
            int offset = 0;
            for (int initialization = 0; initialization < initializations; initialization++)
            {
                for (int qp = 0; qp <= 51; qp++)
                {
                    H264CabacContext[] contexts = create(qp, initialization);
                    if (contexts.Length != indices.Length)
                    {
                        throw new InvalidDataException("The H.264 context set has an unexpected length.");
                    }
                    foreach (int index in indices)
                    {
                        H264CabacContext context = contexts[getIndex(index)];
                        int packed = context.State * 2 + (context.MostProbableSymbol ? 1 : 0);
                        if (packed != expected[offset++])
                        {
                            throw new InvalidDataException($"H.264 context mismatch at initialization {initialization}, QP {qp}, context {index}.");
                        }
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
            H264CabacReader reader = new(ref bits, header);
            if (reader.Read(ref bits, 3) || reader.Read(ref bits, 399))
            {
                throw new InvalidDataException("The H.264 intra reference does not start with the expected 4x4 intra macroblock.");
            }
        }

        public static async Task VerifyRealPredictivePrefixAsync(string root)
        {
            (H264Configuration configuration, byte[] nal) = await H264TestInput.ReadSliceAsync(Path.Combine(root, "avc-aac.mp4"), 1);
            H264BitReader bits = H264Nal.Payload(nal, H264Nal.Type(nal));
            H264SliceHeader header = H264SliceHeaderReader.Read(ref bits, configuration, nal[0]);
            if (!header.Picture.Cabac || header.Kind != H264SliceKind.P || header.Qp != 25 || header.CabacInitIdc != 0)
            {
                throw new InvalidDataException("The H.264 predictive reference has unexpected slice syntax.");
            }
            H264CabacReader reader = new(ref bits, header);
            if (H264CabacMacroblockSyntax.ReadSkip(ref bits, reader, 0) || H264CabacMacroblockSyntax.ReadPredictiveType(ref bits, reader) != 0)
            {
                throw new InvalidDataException("The H.264 predictive reference does not start with the expected non-skipped 16x16 partition.");
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

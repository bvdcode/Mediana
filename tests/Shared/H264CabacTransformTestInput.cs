// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    internal static class H264CabacTransformTestInput
    {
        public static async Task VerifyContextsAsync(string root)
        {
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, "cabac-8x8-contexts.bin"));
            if (expected.Length != 4 * 52 * 34)
            {
                throw new InvalidDataException("The 8x8 context reference has an unexpected length.");
            }
            int offset = 0;
            for (int selection = 0; selection < 4; selection++)
            {
                H264SliceKind kind = selection == 0 ? H264SliceKind.I : H264SliceKind.P;
                for (int qp = 0; qp <= 51; qp++)
                {
                    H264CabacContext[] contexts = H264CabacTransformContexts.Create(qp, kind, Math.Max(0, selection - 1));
                    if (contexts.Length != 34)
                    {
                        throw new InvalidDataException("The 8x8 context set has an unexpected length.");
                    }
                    foreach (H264CabacContext context in contexts)
                    {
                        if (context.State * 2 + (context.MostProbableSymbol ? 1 : 0) != expected[offset++])
                        {
                            throw new InvalidDataException($"8x8 context initialization differs for selection {selection}, QP {qp}.");
                        }
                    }
                }
            }
        }

        public static async Task VerifyResidualsAsync(string root, string mp4Root)
        {
            byte[] states = await File.ReadAllBytesAsync(Path.Combine(root, "cabac-8x8-contexts.bin"));
            byte[] mapping = await File.ReadAllBytesAsync(Path.Combine(root, "cabac-8x8-mapping.bin"));
            byte[] intraStates = await File.ReadAllBytesAsync(Path.Combine(root, "cabac-intra-contexts.bin"));
            byte[] predictiveStates = await File.ReadAllBytesAsync(Path.Combine(root, "cabac-predictive-contexts.bin"));
            if (mapping.Length != 126)
            {
                throw new InvalidDataException("The 8x8 significance map has an unexpected length.");
            }
            H264SliceHeader header = await H264CabacPredictionTestInput.ReadHeaderAsync(mp4Root);
            for (int selection = 0; selection < 4; selection++)
            {
                foreach (int qp in new[] { 0, 25, 41, 51 })
                {
                    List<int[]> blocks = [];
                    for (int position = 0; position < 64; position++)
                    {
                        int[] block = new int[64];
                        block[position] = position % 2 == 0 ? 1 : -1;
                        blocks.Add(block);
                    }
                    AddSymmetricBlocks(blocks);
                    blocks.Add(Enumerable.Range(0, 64).Select(index => index % 2 == 0 ? index + 1 : -index - 1).ToArray());
                    blocks.Add(Enumerable.Range(0, 64).Select(index => index == 0 ? -32768 : index == 63 ? 32767 : 0).ToArray());
                    H264CabacResidualWriter writer = new(qp, selection == 0 ? intraStates : predictiveStates, selection - 1);
                    writer.Initialize8x8(states);
                    foreach (int[] block in blocks)
                    {
                        writer.Write(5, 0, block, mapping);
                    }
                    byte[] payload = writer.Finish();
                    H264BitReader bits = new(payload);
                    H264SliceKind kind = selection == 0 ? H264SliceKind.I : H264SliceKind.P;
                    H264SliceHeader blockHeader = header with { Qp = qp, Kind = kind, CabacInitIdc = Math.Max(0, selection - 1) };
                    H264CabacReader reader = new(ref bits, blockHeader);
                    foreach (int[] block in blocks)
                    {
                        int[] actual = new int[64];
                        int total = H264CabacResidual.Read(ref bits, reader, H264ResidualCategory.Luma8x8, 0, actual);
                        if (total != block.Count(value => value != 0) || !actual.AsSpan().SequenceEqual(block))
                        {
                            throw new InvalidDataException($"8x8 residual differs for selection {selection}, QP {qp}.");
                        }
                    }
                    if (!reader.Termination(ref bits))
                    {
                        throw new InvalidDataException("The 8x8 residual interval did not terminate.");
                    }
                    if (H264VectorInverse8x8.SupportsPrepared(qp))
                    {
                        VerifyPrepared(payload, blockHeader, blocks, bits.BitPosition);
                    }
                }
            }
        }

        private static void AddSymmetricBlocks(List<int[]> blocks)
        {
            ReadOnlySpan<int> scan = H264CoefficientScans.EightByEight(false);
            foreach (int oddAxes in new[] { 1, 8, 9 })
            {
                int[] block = new int[64];
                for (int index = 0; index < block.Length; index++)
                {
                    int position = scan[index];
                    if ((position & oddAxes) == 0)
                    {
                        block[index] = index % 2 == 0 ? 1 + index : -1 - index;
                    }
                }
                blocks.Add(block);
            }
        }

        private static void VerifyPrepared(byte[] payload, H264SliceHeader header, List<int[]> blocks, int finalPosition)
        {
            H264BitReader bits = new(payload);
            H264CabacReader reader = new(ref bits, header);
            const int stride = 13;
            const int sentinel = int.MinValue;
            Span<int> coefficients = stackalloc int[64];
            Span<int> residual = stackalloc int[64];
            Span<byte> prediction = stackalloc byte[stride * 8];
            Span<byte> expected = stackalloc byte[stride * 8];
            Span<byte> actual = stackalloc byte[stride * 8];
            new Random(9541).NextBytes(prediction);
            foreach (int[] block in blocks)
            {
                coefficients.Fill(sentinel);
                H264CabacResidual.ReadPreparedLuma8x8(ref bits, reader, header.Qp, coefficients,
                    out H264ResidualSymmetry symmetry, out int dcLevel);
                if (dcLevel != 0)
                {
                    if (dcLevel != block[0] || block.AsSpan(1).ContainsAnyExcept(0) || coefficients.ContainsAnyExcept(sentinel))
                    {
                        throw new InvalidDataException("Preparing a DC-only block changed its coefficient storage.");
                    }
                    continue;
                }

                H264InverseTransform8x8.Residual(block, header.Qp, residual);
                expected.Fill(0xC7);
                actual.Fill(0xC7);
                for (int row = 0; row < 8; row++)
                {
                    for (int column = 0; column < 8; column++)
                    {
                        int pixel = row * stride + column;
                        expected[pixel] = (byte)Math.Clamp(prediction[pixel] + residual[row * 8 + column], 0, 255);
                    }
                }
                H264VectorInverse8x8.WritePrepared(coefficients, symmetry, prediction, stride, actual, stride);
                if (!actual.SequenceEqual(expected) || coefficients[H264VectorInverse8x8.PreparedLength(symmetry)..].ContainsAnyExcept(sentinel))
                {
                    throw new InvalidDataException($"Prepared 8x8 reconstruction differs at QP {header.Qp}, symmetry {symmetry}.");
                }
            }
            if (!reader.Termination(ref bits) || bits.BitPosition != finalPosition)
            {
                throw new InvalidDataException("The prepared 8x8 residual interval did not terminate at the expected bit position.");
            }
        }
    }
}

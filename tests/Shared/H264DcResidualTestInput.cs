// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    internal static class H264DcResidualTestInput
    {
        private const int Guard = 0x12345678;

        public static void VerifyDcOnly()
        {
            int[] coefficients = [int.MinValue, -65536, -1024, -65, -64, -63, -33, -32, -31, -1,
                0, 1, 31, 32, 33, 63, 64, 65, 1024, 65536, int.MaxValue];
            int[] scales = [10, 11, 13, 14, 16, 18];
            for (int qp = 0; qp <= 51; qp++)
            {
                foreach (int coefficient in coefficients)
                {
                    BigInteger scaled = (BigInteger)coefficient * scales[qp % 6] * BigInteger.Pow(2, qp / 6);
                    VerifyConstant(qp, coefficient, null, scaled);
                    VerifyConstant(qp, 0, coefficient, coefficient);
                }
            }
        }

        public static void VerifyAcBlocks()
        {
            int[] source = new int[16];
            int[] guarded = new int[18];
            foreach (int qp in new[] { 0, 51 })
            {
                foreach (int level in new[] { -1024, 1024 })
                {
                    for (int position = 1; position < 16; position++)
                    {
                        Array.Clear(source);
                        source[0] = 64;
                        source[position] = level;
                        guarded.AsSpan().Fill(Guard);
                        H264InverseTransform.Residual4x4(source, qp, guarded.AsSpan(1, 16));
                        VerifyNonconstant(guarded);
                        H264InverseTransform.Residual4x4(source.AsSpan(1), qp, guarded.AsSpan(1, 16), 640);
                        VerifyNonconstant(guarded);
                    }
                }
            }
        }

        public static void VerifyOverlappingStorage()
        {
            for (int offset = 0; offset <= 2; offset++)
            {
                foreach (bool separateDc in new[] { false, true })
                {
                    int[] storage = new int[20];
                    storage.AsSpan().Fill(Guard);
                    Span<int> source = storage.AsSpan(1, separateDc ? 15 : 16);
                    source.Clear();
                    if (!separateDc)
                    {
                        source[0] = 64;
                    }
                    int[] before = storage.ToArray();
                    H264InverseTransform.Residual4x4(source, 0, storage.AsSpan(offset, 16), separateDc ? 640 : null);
                    for (int index = 0; index < storage.Length; index++)
                    {
                        int expected = index >= offset && index < offset + 16 ? 10 : before[index];
                        if (storage[index] != expected)
                        {
                            throw new InvalidDataException("An overlapping DC block changed its residual or adjacent storage.");
                        }
                    }
                }
            }
        }

        private static void VerifyConstant(int qp, int coefficient, int? separateDc, BigInteger scaled)
        {
            BigInteger expected = BigInteger.DivRem(scaled + 32, 64, out BigInteger remainder);
            if (remainder.Sign < 0)
            {
                expected--;
            }
            bool overflowExpected = expected < int.MinValue || expected > int.MaxValue;
            int[] source = new int[16];
            source[0] = coefficient;
            int[] guarded = new int[18];
            guarded.AsSpan().Fill(Guard);
            bool overflow = false;
            try
            {
                H264InverseTransform.Residual4x4(source.AsSpan(0, separateDc is null ? 16 : 15), qp,
                    guarded.AsSpan(1, 16), separateDc);
            }
            catch (OverflowException)
            {
                overflow = true;
            }
            if (overflow != overflowExpected || guarded[0] != Guard || guarded[^1] != Guard
                || source[0] != coefficient || source.AsSpan(1).ContainsAnyExcept(0))
            {
                throw new InvalidDataException("DC transform overflow, source ownership or destination bounds changed.");
            }
            int value = overflowExpected ? Guard : (int)expected;
            if (guarded.AsSpan(1, 16).ContainsAnyExcept(value))
            {
                throw new InvalidDataException("A DC-only residual differs from exact integer division and signed rounding.");
            }
        }

        private static void VerifyNonconstant(ReadOnlySpan<int> guarded)
        {
            ReadOnlySpan<int> residual = guarded.Slice(1, 16);
            if (guarded[0] != Guard || guarded[^1] != Guard || !residual.ContainsAnyExcept(residual[0]))
            {
                throw new InvalidDataException("A nonzero AC coefficient was discarded or exceeded destination bounds.");
            }
        }
    }
}
